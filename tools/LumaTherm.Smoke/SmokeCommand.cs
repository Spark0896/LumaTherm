using System.Text.Json;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Sensors;

namespace LumaTherm.Smoke;

public static class SmokeCommand
{
    private static readonly RgbColor[] CycleColors =
    [
        new(0x50, 0xC8, 0xFF),
        new(0xFF, 0xC6, 0x4A),
        new(0xFF, 0x56, 0x5D),
    ];

    public static async Task<int> RunAsync(
        string[] arguments,
        ITemperatureProvider normalTemperatureProvider,
        ITemperatureProvider fallbackTemperatureProvider,
        ILightingController lightingController,
        TextWriter output,
        TextReader input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(normalTemperatureProvider);
        ArgumentNullException.ThrowIfNull(fallbackTemperatureProvider);
        ArgumentNullException.ThrowIfNull(lightingController);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);

        var options = SmokeOptions.Parse(arguments);
        if (options.Error is not null)
        {
            await WriteAsync(output, options.Json, new { status = "error", message = options.Error }, options.Error).ConfigureAwait(false);
            return 2;
        }

        try
        {
            return options.Command switch
            {
                "sensor" => await RunSensorAsync(options, normalTemperatureProvider, fallbackTemperatureProvider, output, cancellationToken).ConfigureAwait(false),
                "lights" => await RunLightsAsync(options, lightingController, output, cancellationToken).ConfigureAwait(false),
                "cycle" => await RunCycleAsync(options, lightingController, output, input, cancellationToken).ConfigureAwait(false),
                "simulate" => await RunSimulationAsync(options, lightingController, output, input, cancellationToken).ConfigureAwait(false),
                _ => await WriteUsageAsync(options.Json, output).ConfigureAwait(false),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteAsync(output, options.Json, new { status = "cancelled" }, "Операция отменена.").ConfigureAwait(false);
            return 3;
        }
        catch (Exception exception)
        {
            await WriteAsync(output, options.Json, new { status = "error", message = exception.Message }, $"Ошибка: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RunSensorAsync(
        SmokeOptions options,
        ITemperatureProvider normalTemperatureProvider,
        ITemperatureProvider fallbackTemperatureProvider,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var provider = options.SkipNvml ? fallbackTemperatureProvider : normalTemperatureProvider;
        var reading = await provider.TryReadAsync(cancellationToken).ConfigureAwait(false);
        if (reading is null)
        {
            var message = options.SkipNvml
                ? "MSI Afterburner не предоставил температуру GPU. Убедитесь, что Afterburner запущен и shared memory включена."
                : "NVML и MSI Afterburner не предоставили температуру GPU. Проверьте драйвер NVIDIA или shared memory Afterburner.";
            await WriteAsync(output, options.Json, new { status = "error", message }, message).ConfigureAwait(false);
            return 1;
        }

        var text = $"Провайдер: {reading.SourceName}; GPU: {reading.DeviceName}; Температура: {reading.Celsius.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} °C";
        await WriteAsync(output, options.Json, new { status = "pass", provider = reading.SourceName, gpu = reading.DeviceName, temperatureC = reading.Celsius }, text).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunLightsAsync(SmokeOptions options, ILightingController lightingController, TextWriter output, CancellationToken cancellationToken)
    {
        var devices = await lightingController.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var text = devices.Count == 0
            ? "Windows Dynamic Lighting не обнаружил LampArray. Включите Dynamic Lighting и проверьте конфликтующие контроллеры."
            : string.Join(Environment.NewLine, devices.Select(device => $"{device.Name}; id: {device.Id}; lamps: {device.LampCount}; available: {device.IsAvailable}"));
        await WriteAsync(output, options.Json, new
        {
            status = devices.Count == 0 ? "error" : "pass",
            devices = devices.Select(device => new { id = device.Id, name = device.Name, lampCount = device.LampCount, available = device.IsAvailable }),
        }, text).ConfigureAwait(false);
        return devices.Count == 0 ? 1 : 0;
    }

    private static async Task<int> RunCycleAsync(SmokeOptions options, ILightingController lightingController, TextWriter output, TextReader input, CancellationToken cancellationToken)
    {
        if (!await ConfirmWriteAsync(options, output, input).ConfigureAwait(false))
        {
            return 2;
        }

        return await UseLightsAsync(options, lightingController, output, cancellationToken, async token =>
        {
            for (var index = 0; index < CycleColors.Length; index++)
            {
                await lightingController.SetColorAsync(CycleColors[index], token).ConfigureAwait(false);
                if (index < CycleColors.Length - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(750), token).ConfigureAwait(false);
                }
            }
        }).ConfigureAwait(false);
    }

    private static async Task<int> RunSimulationAsync(SmokeOptions options, ILightingController lightingController, TextWriter output, TextReader input, CancellationToken cancellationToken)
    {
        if (!await ConfirmWriteAsync(options, output, input).ConfigureAwait(false))
        {
            return 2;
        }

        if (!options.TryGetSimulation(out var from, out var to, out var seconds, out var error))
        {
            await WriteAsync(output, options.Json, new { status = "error", message = error }, error).ConfigureAwait(false);
            return 2;
        }

        return await UseLightsAsync(options, lightingController, output, cancellationToken, async token =>
        {
            var steps = Math.Max(2, checked((int)Math.Ceiling(seconds * 10)));
            var elapsed = TimeSpan.FromSeconds(seconds / steps);
            var engine = new ColorEngine(ThermalProfile.Default, from);
            for (var index = 0; index <= steps; index++)
            {
                var temperature = from + ((to - from) * index / steps);
                await lightingController.SetColorAsync(engine.Step(temperature, elapsed), token).ConfigureAwait(false);
                if (index < steps)
                {
                    await Task.Delay(elapsed, token).ConfigureAwait(false);
                }
            }
        }).ConfigureAwait(false);
    }

    private static async Task<int> UseLightsAsync(SmokeOptions options, ILightingController lightingController, TextWriter output, CancellationToken cancellationToken, Func<CancellationToken, Task> work)
    {
        try
        {
            if (!await lightingController.ConnectAsync(null, cancellationToken).ConfigureAwait(false))
            {
                await WriteAsync(output, options.Json, new { status = "error", message = "LampArray недоступен." }, "LampArray недоступен. Выполните `lights` для безопасной диагностики.").ConfigureAwait(false);
                return 1;
            }

            await work(cancellationToken).ConfigureAwait(false);
            await WriteAsync(output, options.Json, new { status = "pass" }, "Подсветка освобождена.").ConfigureAwait(false);
            return 0;
        }
        finally
        {
            using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await lightingController.ReleaseAsync(releaseTimeout.Token).ConfigureAwait(false);
        }
    }

    private static async Task<bool> ConfirmWriteAsync(SmokeOptions options, TextWriter output, TextReader input)
    {
        if (!options.ConfirmLightWrite)
        {
            await WriteAsync(output, options.Json, new { status = "error", message = "Требуется --confirm-light-write." }, "Для записи подсветки требуется --confirm-light-write.").ConfigureAwait(false);
            return false;
        }

        if (options.NonInteractive)
        {
            return true;
        }

        await output.WriteAsync("LumaTherm временно возьмёт управление Windows Dynamic Lighting и освободит его после проверки. Введите YES для продолжения: ").ConfigureAwait(false);
        return string.Equals(await input.ReadLineAsync().ConfigureAwait(false), "YES", StringComparison.Ordinal);
    }

    private static async Task<int> WriteUsageAsync(bool json, TextWriter output)
    {
        await WriteAsync(
            output,
            json,
            new { status = "error", message = "Неизвестная команда." },
            "Использование: sensor [--skip-nvml] [--json] | lights [--json] | cycle --confirm-light-write [--non-interactive] [--json] | simulate --from 35 --to 85 --seconds 10 --confirm-light-write [--non-interactive] [--json]").ConfigureAwait(false);
        return 2;
    }

    private static Task WriteAsync(TextWriter output, bool json, object result, string text) =>
        output.WriteLineAsync(json ? JsonSerializer.Serialize(result) : text);

    private sealed record SmokeOptions
    {
        public string Command { get; private init; } = string.Empty;
        public bool Json { get; private init; }
        public bool SkipNvml { get; private init; }
        public bool ConfirmLightWrite { get; private init; }
        public bool NonInteractive { get; private init; }
        public string? From { get; private init; }
        public string? To { get; private init; }
        public string? Seconds { get; private init; }
        public string? Error { get; private init; }

        public static SmokeOptions Parse(IReadOnlyList<string> arguments)
        {
            if (arguments.Count == 0)
            {
                return new SmokeOptions { Error = "Команда не указана." };
            }

            var options = new SmokeOptions { Command = arguments[0] };
            for (var index = 1; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                options = argument switch
                {
                    "--json" => options with { Json = true },
                    "--skip-nvml" => options with { SkipNvml = true },
                    "--confirm-light-write" => options with { ConfirmLightWrite = true },
                    "--non-interactive" => options with { NonInteractive = true },
                    "--from" or "--to" or "--seconds" when index + 1 < arguments.Count => options.SetValue(argument, arguments[++index]),
                    _ => options with { Error = $"Неизвестный или неполный аргумент: {argument}" },
                };
                if (options.Error is not null)
                {
                    return options;
                }
            }

            return options;
        }

        public bool TryGetSimulation(out double from, out double to, out double seconds, out string error)
        {
            from = 0;
            to = 0;
            seconds = 0;
            if (!double.TryParse(From, System.Globalization.CultureInfo.InvariantCulture, out from) || !double.TryParse(To, System.Globalization.CultureInfo.InvariantCulture, out to) || !double.TryParse(Seconds, System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                error = "Для simulate укажите --from, --to и --seconds с числами.";
                return false;
            }

            if (!double.IsFinite(from) || !double.IsFinite(to) || from is < 0 or > 120 || to is < 0 or > 120 || seconds is <= 0 or > 60)
            {
                error = "simulate принимает температуру от 0 до 120 °C и длительность от 0 до 60 секунд.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private SmokeOptions SetValue(string name, string value) => name switch
        {
            "--from" => this with { From = value },
            "--to" => this with { To = value },
            "--seconds" => this with { Seconds = value },
            _ => this,
        };

    }
}
