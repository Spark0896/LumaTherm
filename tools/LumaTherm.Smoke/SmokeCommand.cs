using System.Text.Json;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Sensors;

namespace LumaTherm.Smoke;

public static class SmokeCommand
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(2);
    private static readonly RgbColor[] CycleColors = ThermalProfile.Default.Points.Select(point => point.Color).ToArray();

    public static Task<int> RunAsync(string[] arguments, ITemperatureProvider normal, ITemperatureProvider fallback, ILightingController lights, TextWriter output, TextReader input, CancellationToken cancellationToken) =>
        RunAsync(arguments, normal, fallback, lights, output, TextWriter.Null, input, cancellationToken);

    public static async Task<int> RunAsync(string[] arguments, ITemperatureProvider normal, ITemperatureProvider fallback, ILightingController lights, TextWriter output, TextWriter prompt, TextReader input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(normal);
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(input);

        var options = SmokeOptions.Parse(arguments);
        var result = options.Error is not null
            ? SmokeResult.Error(options.Error, 2)
            : await ExecuteAsync(options, normal, fallback, lights, prompt, input, cancellationToken).ConfigureAwait(false);
        var disposalFailure = await DisposeResourcesAsync(normal, fallback, lights, result.SkipLightingDispose).ConfigureAwait(false);
        if (disposalFailure is not null)
        {
            result = result.WithCleanupFailure(disposalFailure);
        }

        await output.WriteLineAsync(options.Json ? JsonSerializer.Serialize(result.Json) : result.Text).ConfigureAwait(false);
        return result.ExitCode;
    }

    private static async Task<SmokeResult> ExecuteAsync(SmokeOptions options, ITemperatureProvider normal, ITemperatureProvider fallback, ILightingController lights, TextWriter prompt, TextReader input, CancellationToken token)
    {
        try
        {
            return options.Command switch
            {
                "sensor" => await SensorAsync(options, normal, fallback, token).ConfigureAwait(false),
                "lights" => await LightsAsync(lights, token).ConfigureAwait(false),
                "cycle" => await CycleAsync(options, lights, prompt, input, token).ConfigureAwait(false),
                "simulate" => await SimulateAsync(options, lights, prompt, input, token).ConfigureAwait(false),
                _ => SmokeResult.Error("Неизвестная команда.", 2),
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return SmokeResult.Cancelled();
        }
        catch (Exception exception)
        {
            return SmokeResult.Error($"Ошибка: {exception.Message}", 1);
        }
    }

    private static async Task<SmokeResult> SensorAsync(SmokeOptions options, ITemperatureProvider normal, ITemperatureProvider fallback, CancellationToken token)
    {
        var reading = await (options.SkipNvml ? fallback : normal).TryReadAsync(token).ConfigureAwait(false);
        if (reading is null)
        {
            return SmokeResult.Error(options.SkipNvml
                ? "MSI Afterburner не предоставил температуру GPU. Убедитесь, что Afterburner запущен и shared memory включена."
                : "NVML и MSI Afterburner не предоставили температуру GPU. Проверьте драйвер NVIDIA или shared memory Afterburner.", 1);
        }

        return SmokeResult.Pass(
            new { status = "pass", provider = reading.SourceName, gpu = reading.DeviceName, temperatureC = reading.Celsius },
            $"Провайдер: {reading.SourceName}; GPU: {reading.DeviceName}; Температура: {reading.Celsius.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} °C");
    }

    private static async Task<SmokeResult> LightsAsync(ILightingController lights, CancellationToken token)
    {
        var devices = await lights.DiscoverAsync(token).ConfigureAwait(false);
        if (devices.Count == 0)
        {
            return SmokeResult.Error("Windows Dynamic Lighting не обнаружил LampArray. Включите Dynamic Lighting и проверьте конфликтующие контроллеры.", 1);
        }

        return SmokeResult.Pass(
            new { status = "pass", devices = devices.Select(device => new { id = device.Id, name = device.Name, lampCount = device.LampCount, available = device.IsAvailable }) },
            string.Join(Environment.NewLine, devices.Select(device => $"{device.Name}; id: {device.Id}; lamps: {device.LampCount}; available: {device.IsAvailable}")));
    }

    private static async Task<SmokeResult> CycleAsync(SmokeOptions options, ILightingController lights, TextWriter prompt, TextReader input, CancellationToken token)
    {
        var confirmation = await ConfirmAsync(options, prompt, input, token).ConfigureAwait(false);
        if (confirmation is not null) return confirmation;
        return await UseLightsAsync(lights, async workToken =>
        {
            for (var index = 0; index < CycleColors.Length; index++)
            {
                await lights.SetColorAsync(CycleColors[index], workToken).ConfigureAwait(false);
                if (index < CycleColors.Length - 1) await Task.Delay(TimeSpan.FromMilliseconds(750), workToken).ConfigureAwait(false);
            }
        }, token).ConfigureAwait(false);
    }

    private static async Task<SmokeResult> SimulateAsync(SmokeOptions options, ILightingController lights, TextWriter prompt, TextReader input, CancellationToken token)
    {
        if (!options.TryGetSimulation(out var from, out var to, out var seconds, out var error)) return SmokeResult.Error(error, 2);
        var confirmation = await ConfirmAsync(options, prompt, input, token).ConfigureAwait(false);
        if (confirmation is not null) return confirmation;
        return await UseLightsAsync(lights, async workToken =>
        {
            var steps = Math.Max(2, checked((int)Math.Ceiling(seconds * 10)));
            var elapsed = TimeSpan.FromSeconds(seconds / steps);
            var engine = new ColorEngine(ThermalProfile.Default, from);
            for (var index = 0; index <= steps; index++)
            {
                await lights.SetColorAsync(engine.Step(from + ((to - from) * index / steps), elapsed), workToken).ConfigureAwait(false);
                if (index < steps) await Task.Delay(elapsed, workToken).ConfigureAwait(false);
            }
        }, token).ConfigureAwait(false);
    }

    private static async Task<SmokeResult?> ConfirmAsync(SmokeOptions options, TextWriter prompt, TextReader input, CancellationToken token)
    {
        if (!options.ConfirmLightWrite) return SmokeResult.Error("Требуется --confirm-light-write.", 2);
        await prompt.WriteAsync("LumaTherm временно возьмёт управление Windows Dynamic Lighting и освободит его после проверки. Введите YES для продолжения: ").ConfigureAwait(false);
        return string.Equals(await ReadLineWithCancellationAsync(input, token).ConfigureAwait(false), "YES", StringComparison.Ordinal)
            ? null
            : SmokeResult.Error("Подтверждение отклонено. Для записи подсветки введите точное YES.", 2);
    }

    private static async Task<string?> ReadLineWithCancellationAsync(TextReader input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var readTask = Task.Run(input.ReadLine);
        try
        {
            return await readTask.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _ = ObserveLateReadFaultAsync(readTask);
            throw;
        }
    }

    private static async Task ObserveLateReadFaultAsync(Task<string?> readTask)
    {
        try
        {
            await readTask.ConfigureAwait(false);
        }
        catch
        {
            // A cancelled caller cannot await a synchronous console read; observe its eventual fault.
        }
    }

    private static async Task<SmokeResult> UseLightsAsync(ILightingController lights, Func<CancellationToken, Task> work, CancellationToken token)
    {
        Exception? operationFailure = null;
        var cancelled = false;
        var connected = false;
        try
        {
            connected = await lights.ConnectAsync(null, token).ConfigureAwait(false);
            if (!connected) return SmokeResult.Error("LampArray недоступен. Выполните `lights` для безопасной диагностики.", 1);
            await work(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            cancelled = true;
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }

        var releaseFailure = connected ? await RunBoundedAsync(cleanupToken => lights.ReleaseAsync(cleanupToken)).ConfigureAwait(false) : null;
        if (releaseFailure is not null)
        {
            var message = operationFailure is null
                ? $"Не удалось освободить Dynamic Lighting: {releaseFailure.Message}"
                : $"Проверка подсветки не завершилась: {operationFailure.Message}; освобождение Dynamic Lighting также не удалось: {releaseFailure.Message}";
            return SmokeResult.Error(message, 1, releaseFailure is TimeoutException);
        }

        if (cancelled) return SmokeResult.Cancelled();
        if (operationFailure is not null) return SmokeResult.Error($"Проверка подсветки не завершилась: {operationFailure.Message}", 1);
        return SmokeResult.Pass(new { status = "pass" }, "Подсветка освобождена.");
    }

    private static async Task<Exception?> DisposeResourcesAsync(ITemperatureProvider normal, ITemperatureProvider fallback, ILightingController lights, bool skipLighting)
    {
        var failures = new List<Exception>();
        foreach (var resource in new IAsyncDisposable[] { normal, fallback })
        {
            var failure = await RunBoundedAsync(_ => resource.DisposeAsync().AsTask()).ConfigureAwait(false);
            if (failure is not null) failures.Add(failure);
        }

        if (!skipLighting)
        {
            var failure = await RunBoundedAsync(_ => lights.DisposeAsync().AsTask()).ConfigureAwait(false);
            if (failure is not null) failures.Add(failure);
        }

        return failures.Count switch { 0 => null, 1 => failures[0], _ => new AggregateException(failures) };
    }

    private static async Task<Exception?> RunBoundedAsync(Func<CancellationToken, Task> action)
    {
        using var timeout = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await Task.Run(() => action(timeout.Token)).WaitAsync(timeout.Token).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return new TimeoutException("Очистка не завершилась за 2 секунды; результат PASS не будет выдан.");
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed record SmokeResult(int ExitCode, object Json, string Text, bool SkipLightingDispose)
    {
        public static SmokeResult Pass(object json, string text) => new(0, json, text, false);
        public static SmokeResult Error(string message, int exitCode, bool skipLightingDispose = false) => new(exitCode, new { status = "error", message }, message, skipLightingDispose);
        public static SmokeResult Cancelled() => new(3, new { status = "cancelled", message = "Операция отменена." }, "Операция отменена.", false);
        public SmokeResult WithCleanupFailure(Exception exception) => Error($"{Text}; очистка ресурсов не завершилась: {exception.Message}", 1, SkipLightingDispose);
    }

    private sealed record SmokeOptions
    {
        public string Command { get; private init; } = string.Empty;
        public bool Json { get; private init; }
        public bool SkipNvml { get; private init; }
        public bool ConfirmLightWrite { get; private init; }
        public string? From { get; private init; }
        public string? To { get; private init; }
        public string? Seconds { get; private init; }
        public string? Error { get; private init; }

        public static SmokeOptions Parse(IReadOnlyList<string> arguments)
        {
            if (arguments.Count == 0) return new SmokeOptions { Error = "Команда не указана." };
            var options = new SmokeOptions { Command = arguments[0] };
            for (var index = 1; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                options = argument switch
                {
                    "--json" => options with { Json = true },
                    "--skip-nvml" => options with { SkipNvml = true },
                    "--confirm-light-write" => options with { ConfirmLightWrite = true },
                    "--from" or "--to" or "--seconds" when index + 1 < arguments.Count => options.SetValue(argument, arguments[++index]),
                    _ => options with { Error = $"Неизвестный или неполный аргумент: {argument}" },
                };
                if (options.Error is not null) return options;
            }

            return options;
        }

        public bool TryGetSimulation(out double from, out double to, out double seconds, out string error)
        {
            from = to = seconds = 0;
            if (!double.TryParse(From, System.Globalization.CultureInfo.InvariantCulture, out from) || !double.TryParse(To, System.Globalization.CultureInfo.InvariantCulture, out to) || !double.TryParse(Seconds, System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                error = "Для simulate укажите --from, --to и --seconds с числами.";
                return false;
            }

            if (!double.IsFinite(from) || !double.IsFinite(to) || from is < 0 or > 120 || to is < 0 or > 120 || seconds is < 0.1 or > 60)
            {
                error = "simulate принимает температуру от 0 до 120 °C и длительность от 0.1 до 60 секунд.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private SmokeOptions SetValue(string name, string value) => name switch { "--from" => this with { From = value }, "--to" => this with { To = value }, "--seconds" => this with { Seconds = value }, _ => this };
    }
}
