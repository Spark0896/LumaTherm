using LumaTherm.Core.Colors;

namespace LumaTherm.Core.Settings;

public sealed record AppSettings(
    int SchemaVersion,
    ThermalProfile Profile,
    bool IsModeEnabled,
    bool IsAutostartEnabled,
    bool MinimizeToTray,
    bool NotificationsEnabled,
    string? PreferredLightingDeviceId)
{
    public static AppSettings Default { get; } = new(1, ThermalProfile.Default, false, false, true, true, null);

    public AppSettings Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException($"Unsupported settings schema {SchemaVersion}.");
        Profile.Validate();
        return this;
    }
}
