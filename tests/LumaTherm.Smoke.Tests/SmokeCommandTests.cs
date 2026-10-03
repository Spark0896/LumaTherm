using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Sensors;

namespace LumaTherm.Smoke.Tests;

public sealed class SmokeCommandTests
{
    [Fact]
    public async Task Sensor_PrintsReadingWithoutCreatingLighting()
    {
        var normal = new FakeTemperatureProvider(new TemperatureReading(61.5, "NVML", "NVIDIA GeForce RTX 5070", DateTimeOffset.UnixEpoch));
        var fallback = new FakeTemperatureProvider(null);
        var lights = new FakeLightingController();

        var result = await RunAsync(["sensor"], normal, fallback, lights);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("NVML", result.Output, StringComparison.Ordinal);
        Assert.Contains("NVIDIA GeForce RTX 5070", result.Output, StringComparison.Ordinal);
        Assert.Contains("61.5", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, normal.ReadCount);
        Assert.Equal(0, fallback.ReadCount);
        Assert.Equal(0, lights.DiscoverCount);
        Assert.Equal(0, lights.ConnectCount);
        Assert.Empty(lights.Colors);
    }

    [Fact]
    public async Task Sensor_SkipNvmlUsesOnlyAfterburnerFallback()
    {
        var normal = new FakeTemperatureProvider(new TemperatureReading(61.5, "NVML", "NVIDIA GeForce RTX 5070", DateTimeOffset.UnixEpoch));
        var fallback = new FakeTemperatureProvider(new TemperatureReading(62, "MSI Afterburner", "NVIDIA GeForce RTX 5070", DateTimeOffset.UnixEpoch));
        var lights = new FakeLightingController();

        var result = await RunAsync(["sensor", "--skip-nvml"], normal, fallback, lights);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MSI Afterburner", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("NVML", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, normal.ReadCount);
        Assert.Equal(1, fallback.ReadCount);
        Assert.Equal(0, lights.DiscoverCount);
        Assert.Empty(lights.Colors);
    }

    [Fact]
    public async Task Lights_PrintsDiscoveredDevicesWithoutTakingControl()
    {
        var lights = new FakeLightingController(
            new LightingDeviceInfo("HID#VID_048D&PID_5702", "GIGABYTE Device", 12, true),
            new LightingDeviceInfo("other", "Other Device", 4, false));

        var result = await RunAsync(["lights"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("GIGABYTE Device", result.Output, StringComparison.Ordinal);
        Assert.Contains("HID#VID_048D&PID_5702", result.Output, StringComparison.Ordinal);
        Assert.Contains("12", result.Output, StringComparison.Ordinal);
        Assert.Contains("available", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, lights.DiscoverCount);
        Assert.Equal(0, lights.ConnectCount);
        Assert.Empty(lights.Colors);
        Assert.Equal(0, lights.ReleaseCount);
    }

    [Fact]
    public async Task Cycle_WithoutConfirmationRejectsBeforeAnyLightWrite()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));

        var result = await RunAsync(["cycle"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--confirm-light-write", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, lights.ConnectCount);
        Assert.Empty(lights.Colors);
        Assert.Equal(0, lights.ReleaseCount);
    }

    [Fact]
    public async Task ConfirmedCycle_WritesTheThreeSafetyColorsInOrderAndReleases()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));

        var result = await RunAsync(["cycle", "--confirm-light-write"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["#006BFF", "#3CFF00", "#FF0000"], lights.Colors.Select(color => color.ToHex()));
        Assert.Equal(1, lights.ReleaseCount);
    }

    [Fact]
    public async Task ConfirmedCycle_ReleasesOwnershipWhenTheSecondWriteFails()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true)) { FailOnWrite = 2 };

        var result = await RunAsync(["cycle", "--confirm-light-write"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["#006BFF", "#3CFF00"], lights.Colors.Select(color => color.ToHex()));
        Assert.Equal(1, lights.ReleaseCount);
    }

    [Fact]
    public async Task ConfirmedSimulationUsesSmoothedProductionColorsAndReleasesOwnership()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));

        var result = await RunAsync(["simulate", "--from", "35", "--to", "85", "--seconds", "1", "--confirm-light-write"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(11, lights.Colors.Count);
        Assert.Equal("#006BFF", lights.Colors[0].ToHex());
        Assert.NotEqual(lights.Colors[0], lights.Colors[^1]);
        Assert.All(lights.Colors, color =>
        {
            Assert.Equal(255, new[] { color.R, color.G, color.B }.Max());
            Assert.Equal(0, new[] { color.R, color.G, color.B }.Min());
        });
        Assert.Equal(1, lights.ReleaseCount);
    }

    [Fact]
    public async Task Cycle_NonInteractiveFlagIsRejectedWithoutTakingControl()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));

        var result = await RunAsync(["cycle", "--confirm-light-write", "--non-interactive"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(0, lights.ConnectCount);
        Assert.Empty(lights.Colors);
    }

    [Fact]
    public async Task JsonConfirmation_UsesPromptWriterAndWritesOneParseableResultAfterExactYes()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));

        var result = await RunWithPromptAsync(["simulate", "--from", "35", "--to", "85", "--seconds", "0.1", "--confirm-light-write", "--json"], lights, new StringReader("YES"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Введите YES", result.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Введите YES", result.Output, StringComparison.Ordinal);
        Assert.Equal("pass", JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("NO")]
    [InlineData("")]
    public async Task JsonConfirmation_NonYesOrEofReturnsOneActionableJsonError(string input)
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));

        var result = await RunWithPromptAsync(["cycle", "--confirm-light-write", "--json"], lights, new StringReader(input), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("error", JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
        Assert.Contains("Введите YES", result.Prompt, StringComparison.Ordinal);
        Assert.Equal(0, lights.ConnectCount);
    }

    [Fact]
    public async Task JsonConfirmation_CancellationDuringReadReturnsOneParseableCancelledResultWithinDeadline()
    {
        var reader = new SynchronousBlockingReader();
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));
        using var cancellation = new CancellationTokenSource();
        var task = RunWithPromptAsync(["cycle", "--confirm-light-write", "--json"], lights, TextReader.Synchronized(reader), cancellationToken: cancellation.Token);

        try
        {
            await reader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            cancellation.Cancel();
            var result = await task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.Equal(3, result.ExitCode);
            Assert.Equal("cancelled", JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
            Assert.Equal(0, lights.ConnectCount);
        }
        finally
        {
            reader.Release();
        }
    }

    [Fact]
    public async Task JsonConfirmation_PreCancelledReadReturnsOneParseableCancelledResultWithoutLighting()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await RunWithPromptAsync(["cycle", "--confirm-light-write", "--json"], lights, new BlockingReader(), cancellationToken: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("cancelled", JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
        Assert.Equal(0, lights.ConnectCount);
    }

    [Fact]
    public async Task JsonConfirmation_SynchronizedBlockingReaderCancelsWithinDeadlineWithoutLighting()
    {
        var blockingReader = new SynchronousBlockingReader();
        using var cancellation = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();
        var task = Task.Run(async () => await RunWithPromptAsync(
            ["cycle", "--confirm-light-write", "--json"],
            new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true)),
            TextReader.Synchronized(blockingReader),
            cancellationToken: cancellation.Token));

        try
        {
            await blockingReader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            cancellation.Cancel();
            var result = await task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
            Assert.Equal(3, result.ExitCode);
            Assert.Equal("cancelled", JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
        }
        finally
        {
            blockingReader.Release();
        }
    }

    [Fact]
    public async Task JsonCycle_CancellationAfterWriteReleasesAndReturnsOneCancelledResult()
    {
        using var cancellation = new CancellationTokenSource();
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true))
        {
            OnColorWritten = cancellation.Cancel,
        };

        var result = await RunWithPromptAsync(["cycle", "--confirm-light-write", "--json"], lights, new StringReader("YES"), cancellationToken: cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("cancelled", JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
        Assert.Single(result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(1, lights.ReleaseCount);
    }

    [Fact]
    public async Task JsonSimulation_RejectsDurationBelowOneTenthSecondBeforeLighting()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));

        var result = await RunWithPromptAsync(["simulate", "--from", "35", "--to", "85", "--seconds", "0.09", "--confirm-light-write", "--json"], lights, new StringReader("YES"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("error", JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
        Assert.Equal(0, lights.ConnectCount);
    }

    [Fact]
    public async Task JsonCycle_ReleaseFailureProducesOneErrorInsteadOfPassThenError()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true)) { ReleaseException = new InvalidOperationException("release failed") };

        var result = await RunWithPromptAsync(["simulate", "--from", "35", "--to", "85", "--seconds", "0.1", "--confirm-light-write", "--json"], lights, new StringReader("YES"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("error", JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
        Assert.Single(result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task JsonSimulation_NonCooperativeSynchronousReleaseTimesOutWithoutFalsePass()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true)) { ReleaseDelayMilliseconds = 5000 };
        var stopwatch = Stopwatch.StartNew();

        var result = await RunWithPromptAsync(["simulate", "--from", "35", "--to", "85", "--seconds", "0.1", "--confirm-light-write", "--json"], lights, new StringReader("YES"), cancellationToken: TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(3.5), TestContext.Current.CancellationToken);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3.5));
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("error", JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task JsonSensor_NonCooperativeProviderDisposalTimesOutWithoutAppendingSuccess()
    {
        var normal = new FakeTemperatureProvider(new TemperatureReading(61, "NVML", "RTX", DateTimeOffset.UnixEpoch)) { DisposeDelayMilliseconds = 5000 };
        var stopwatch = Stopwatch.StartNew();

        var result = await RunWithPromptAsync(["sensor", "--json"], new FakeLightingController(), new StringReader("YES"), normal: normal, cancellationToken: TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(3.5), TestContext.Current.CancellationToken);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3.5));
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("error", JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
        Assert.Single(result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string[] arguments,
        ITemperatureProvider normal,
        ITemperatureProvider fallback,
        ILightingController lights)
    {
        var assembly = Assembly.Load("LumaTherm.Smoke");
        var commandType = assembly.GetType("LumaTherm.Smoke.SmokeCommand")
            ?? throw new InvalidOperationException("SmokeCommand does not exist yet.");
        var method = commandType.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(candidate => candidate.Name == "RunAsync" && candidate.GetParameters().Length == 7)
            ?? throw new InvalidOperationException("SmokeCommand.RunAsync does not exist yet.");
        using var output = new StringWriter();
        var task = (Task<int>)method.Invoke(null, [arguments, normal, fallback, lights, output, new StringReader("YES"), TestContext.Current.CancellationToken])!;
        return (await task, output.ToString());
    }

    private static async Task<(int ExitCode, string Output, string Prompt)> RunWithPromptAsync(
        string[] arguments,
        ILightingController lights,
        TextReader input,
        ITemperatureProvider? normal = null,
        CancellationToken cancellationToken = default)
    {
        var assembly = Assembly.Load("LumaTherm.Smoke");
        var commandType = assembly.GetType("LumaTherm.Smoke.SmokeCommand")
            ?? throw new InvalidOperationException("SmokeCommand does not exist yet.");
        var method = commandType.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(candidate => candidate.Name == "RunAsync" && candidate.GetParameters().Length == 8)
            ?? throw new InvalidOperationException("SmokeCommand.RunAsync with a prompt writer does not exist yet.");
        using var output = new StringWriter();
        using var prompt = new StringWriter();
        var task = (Task<int>)method.Invoke(null, [arguments, normal ?? new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights, output, prompt, input, cancellationToken])!;
        return (await task, output.ToString(), prompt.ToString());
    }

    private sealed class FakeTemperatureProvider(TemperatureReading? reading) : ITemperatureProvider
    {
        public int ReadCount { get; private set; }
        public int DisposeDelayMilliseconds { get; init; }

        public ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return ValueTask.FromResult(reading);
        }

        public ValueTask DisposeAsync()
        {
            if (DisposeDelayMilliseconds > 0)
            {
                Thread.Sleep(DisposeDelayMilliseconds);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeLightingController(params LightingDeviceInfo[] devices) : ILightingController
    {
        private readonly IReadOnlyList<LightingDeviceInfo> _devices = devices;

        public int DiscoverCount { get; private set; }
        public int ConnectCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public int FailOnWrite { get; init; }
        public Exception? ReleaseException { get; init; }
        public int ReleaseDelayMilliseconds { get; init; }
        public Action? OnColorWritten { get; init; }
        public List<RgbColor> Colors { get; } = [];
        public bool IsConnected { get; private set; }
        public LightingDeviceInfo? ConnectedDevice => IsConnected ? _devices.FirstOrDefault(device => device.IsAvailable) : null;
        public event EventHandler? DevicesChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiscoverCount++;
            return Task.FromResult(_devices);
        }

        public Task<bool> ConnectAsync(string? preferredDeviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCount++;
            IsConnected = _devices.Any(device => device.IsAvailable);
            return Task.FromResult(IsConnected);
        }

        public Task SetColorAsync(RgbColor color, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Colors.Add(color);
            OnColorWritten?.Invoke();
            if (Colors.Count == FailOnWrite)
            {
                throw new InvalidOperationException("Write failed.");
            }

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(CancellationToken cancellationToken)
        {
            if (ReleaseDelayMilliseconds > 0)
            {
                Thread.Sleep(ReleaseDelayMilliseconds);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;
            IsConnected = false;
            if (ReleaseException is not null)
            {
                throw ReleaseException;
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingReader : TextReader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class SynchronousBlockingReader : TextReader
    {
        private readonly ManualResetEventSlim _released = new();

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string? ReadLine()
        {
            Started.TrySetResult();
            _released.Wait(TimeSpan.FromSeconds(5));
            return null;
        }

        public void Release() => _released.Set();
    }
}
