using LumaTherm.Core.Settings;
using LumaTherm.Infrastructure.System;

namespace LumaTherm.Infrastructure.Tests.System;

public sealed class StartupServiceTests
{
    [Fact]
    public async Task PackagedService_MapsStatesAndEnableDisableResults()
    {
        var platform = new FakeStartupTaskPlatform { State = StartupTaskState.Enabled };
        var service = new PackagedStartupService(platform);

        Assert.True(await service.GetEnabledAsync(CancellationToken.None));

        platform.State = StartupTaskState.EnabledByPolicy;
        Assert.False(await service.GetEnabledAsync(CancellationToken.None));
        platform.EnableResult = StartupTaskState.EnabledByPolicy;
        await service.SetEnabledAsync(true, CancellationToken.None);
        await service.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(1, platform.EnableCalls);
        Assert.Equal(1, platform.DisableCalls);
    }

    [Theory]
    [InlineData(StartupTaskState.DisabledByUser)]
    [InlineData(StartupTaskState.DisabledByPolicy)]
    [InlineData(StartupTaskState.Disabled)]
    public async Task PackagedService_UsesSafeFailureForEnableDenial(StartupTaskState state)
    {
        var platform = new FakeStartupTaskPlatform { EnableResult = state };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PackagedStartupService(platform).SetEnabledAsync(true, CancellationToken.None));

        Assert.Contains("автозапуск", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackagedService_ObservesCancellationBeforePlatformSideEffect()
    {
        var platform = new FakeStartupTaskPlatform();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new PackagedStartupService(platform).SetEnabledAsync(true, cancellation.Token));

        Assert.Equal(0, platform.EnableCalls);
    }

    [Fact]
    public async Task RegistryService_WritesExactQuotedCommandAndDeletesOnlyNamedValueIdempotently()
    {
        var registry = new FakeRegistryRunPlatform();
        var service = new RegistryStartupService(registry, @"C:\Program Files\LumaTherm\LumaTherm.exe");

        await service.SetEnabledAsync(true, CancellationToken.None);

        Assert.True(await service.GetEnabledAsync(CancellationToken.None));
        Assert.Equal(RegistryStartupService.RunKeyPath, registry.LastKeyPath);
        Assert.Equal("LumaTherm", registry.LastValueName);
        Assert.Equal("\"C:\\Program Files\\LumaTherm\\LumaTherm.exe\" --autostart", registry.Value);

        await service.SetEnabledAsync(false, CancellationToken.None);
        await service.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(["LumaTherm", "LumaTherm"], registry.DeletedNames);
        Assert.Null(registry.Value);
        Assert.Equal(0, registry.RealAccessCount);
    }

    [Fact]
    public async Task Selector_UsesPackagedWhenIdentityExistsAndPortableOnlyWhenUnavailable()
    {
        var packaged = new RecordingStartupService();
        var portable = new RecordingStartupService();
        var packagedSelector = new StartupService(new FakePackageIdentityProbe(true), packaged, portable);

        await packagedSelector.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal(1, packaged.SetCalls);
        Assert.Equal(0, portable.SetCalls);

        var portableSelector = new StartupService(new FakePackageIdentityProbe(new PackageIdentityUnavailableException()), packaged, portable);
        await portableSelector.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(1, portable.SetCalls);
    }

    [Fact]
    public async Task Selector_DoesNotSwallowUnrelatedIdentityFailure()
    {
        var expected = new UnauthorizedAccessException("identity failure");
        var service = new StartupService(
            new FakePackageIdentityProbe(expected),
            new RecordingStartupService(),
            new RecordingStartupService());

        var actual = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetEnabledAsync(CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Construction_DoesNotEnableStartup_AndDefaultRemainsOff()
    {
        var packaged = new RecordingStartupService();
        var portable = new RecordingStartupService();
        _ = new StartupService(new FakePackageIdentityProbe(true), packaged, portable);

        Assert.False(AppSettings.Default.IsAutostartEnabled);
        Assert.Equal(0, packaged.SetCalls);
        Assert.Equal(0, portable.SetCalls);
    }

    private sealed class FakeStartupTaskPlatform : IStartupTaskPlatform
    {
        public StartupTaskState State { get; set; } = StartupTaskState.Disabled;
        public StartupTaskState EnableResult { get; set; } = StartupTaskState.Enabled;
        public int EnableCalls { get; private set; }
        public int DisableCalls { get; private set; }
        public Task<StartupTaskState> GetStateAsync(CancellationToken cancellationToken) => Task.FromResult(State);
        public Task<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
        {
            EnableCalls++;
            return Task.FromResult(EnableResult);
        }
        public Task DisableAsync(CancellationToken cancellationToken)
        {
            DisableCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRegistryRunPlatform : IRegistryRunPlatform
    {
        public string? LastKeyPath { get; private set; }
        public string? LastValueName { get; private set; }
        public string? Value { get; private set; }
        public List<string> DeletedNames { get; } = [];
        public int RealAccessCount => 0;
        public string? Read(string keyPath, string valueName)
        {
            LastKeyPath = keyPath;
            LastValueName = valueName;
            return Value;
        }
        public void Write(string keyPath, string valueName, string value)
        {
            LastKeyPath = keyPath;
            LastValueName = valueName;
            Value = value;
        }
        public void Delete(string keyPath, string valueName)
        {
            LastKeyPath = keyPath;
            LastValueName = valueName;
            DeletedNames.Add(valueName);
            Value = null;
        }
    }

    private sealed class RecordingStartupService : LumaTherm.Core.System.IStartupService
    {
        public int SetCalls { get; private set; }
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            SetCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePackageIdentityProbe : IPackageIdentityProbe
    {
        private readonly bool _isPackaged;
        private readonly Exception? _exception;
        public FakePackageIdentityProbe(bool isPackaged) => _isPackaged = isPackaged;
        public FakePackageIdentityProbe(Exception exception) => _exception = exception;
        public bool IsPackaged()
        {
            if (_exception is not null) throw _exception;
            return _isPackaged;
        }
    }
}
