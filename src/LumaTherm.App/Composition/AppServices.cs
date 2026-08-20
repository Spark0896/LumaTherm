using LumaTherm.Core.Diagnostics;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;
using LumaTherm.Infrastructure.Sensors;

namespace LumaTherm.App.Composition;

public interface IAppInstanceCoordinator : IAsyncDisposable
{
    event EventHandler? ActivationRequested;
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken);
    Task SignalActivationAsync(CancellationToken cancellationToken);
}

public interface IAppUiSession : IDisposable
{
    void Show();
    void ShowRestoreActivate();
    void ShowForegroundError(string message);
}

public interface IAppTraySession : IAsyncDisposable
{
    void ShowRecoveryWarning(string message);
    void ShowBackgroundError(string message);
}

public interface IAppDiscoverySession : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);
}

public sealed class AppServices
{
    public AppServices(
        Func<IAppLogger> createLogger,
        Func<IAppLogger, IAppInstanceCoordinator> createSingleInstance,
        Func<ISessionSentinel> createSentinel,
        Func<ISettingsStore> createSettingsStore,
        Func<AppSettings, ISettingsStore, IThermalRuntime> createRuntime,
        Func<IThermalRuntime, IStartupService> createStartupService,
        Func<IThermalRuntime, IStartupService, AppSettings, IAppUiSession> createUi,
        Func<IAppUiSession, IThermalRuntime, IAppTraySession> createTray,
        Func<IThermalRuntime, IAsyncDisposable> createPower,
        Func<IThermalRuntime, IAppUiSession, IAppDiscoverySession> createDiscovery,
        Action<Action> dispatchToUi)
    {
        CreateLogger = createLogger ?? throw new ArgumentNullException(nameof(createLogger));
        CreateSingleInstance = createSingleInstance ?? throw new ArgumentNullException(nameof(createSingleInstance));
        CreateSentinel = createSentinel ?? throw new ArgumentNullException(nameof(createSentinel));
        CreateSettingsStore = createSettingsStore ?? throw new ArgumentNullException(nameof(createSettingsStore));
        CreateRuntime = createRuntime ?? throw new ArgumentNullException(nameof(createRuntime));
        CreateStartupService = createStartupService ?? throw new ArgumentNullException(nameof(createStartupService));
        CreateUi = createUi ?? throw new ArgumentNullException(nameof(createUi));
        CreateTray = createTray ?? throw new ArgumentNullException(nameof(createTray));
        CreatePower = createPower ?? throw new ArgumentNullException(nameof(createPower));
        CreateDiscovery = createDiscovery ?? throw new ArgumentNullException(nameof(createDiscovery));
        DispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));
    }

    internal Func<IAppLogger> CreateLogger { get; }
    internal Func<IAppLogger, IAppInstanceCoordinator> CreateSingleInstance { get; }
    internal Func<ISessionSentinel> CreateSentinel { get; }
    internal Func<ISettingsStore> CreateSettingsStore { get; }
    internal Func<AppSettings, ISettingsStore, IThermalRuntime> CreateRuntime { get; }
    internal Func<IThermalRuntime, IStartupService> CreateStartupService { get; }
    internal Func<IThermalRuntime, IStartupService, AppSettings, IAppUiSession> CreateUi { get; }
    internal Func<IAppUiSession, IThermalRuntime, IAppTraySession> CreateTray { get; }
    internal Func<IThermalRuntime, IAsyncDisposable> CreatePower { get; }
    internal Func<IThermalRuntime, IAppUiSession, IAppDiscoverySession> CreateDiscovery { get; }
    internal Action<Action> DispatchToUi { get; }

    public static IReadOnlyList<string> ProductionTemperatureSourceNames()
    {
        var sources = new LumaTherm.Core.Sensors.ITemperatureSource[]
        {
            new NvmlTemperatureSource(new NvmlApi(), TimeProvider.System),
            new AfterburnerTemperatureSource(new MahmMemoryReader(), TimeProvider.System),
        };
        try
        {
            return sources.Select(source => source.Name).ToArray();
        }
        finally
        {
            foreach (var source in sources) source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
