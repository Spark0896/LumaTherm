using LumaTherm.Core.Settings;

namespace LumaTherm.Core.Runtime;

public interface IThermalRuntime : IAsyncDisposable
{
    event EventHandler<RuntimeSnapshot>? SnapshotChanged;

    RuntimeSnapshot CurrentSnapshot { get; }

    Task StartAsync(CancellationToken cancellationToken);
    Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken);
    Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken);
    Task SuspendAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
