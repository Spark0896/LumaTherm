namespace LumaTherm.Core.Settings;

public interface ISettingsStore
{
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

public sealed record SettingsLoadResult(AppSettings Settings, string? RecoveryMessage = null);
