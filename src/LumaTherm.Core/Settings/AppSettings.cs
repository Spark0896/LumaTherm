using LumaTherm.Core.Colors;

namespace LumaTherm.Core.Settings;

public sealed record AppSettings(
    int SchemaVersion,
    ThermalProfile Profile,
    bool IsModeEnabled,
    bool IsAutostartEnabled,
    bool MinimizeToTray,
    bool NotificationsEnabled,
    string? PreferredLightingDeviceId,
    AppLanguage Language,
    TrayMenuOptions TrayMenu)
{
    public static AppSettings Default { get; } = new(
        2, ThermalProfile.Default, false, false, true, true, null,
        AppLanguage.System, TrayMenuOptions.Default);

    public AppSettings Validate()
    {
        if (SchemaVersion != 2) throw new InvalidDataException($"Unsupported settings schema {SchemaVersion}.");
        if (Profile is null) throw new InvalidDataException("Settings profile is missing.");
        if (!Enum.IsDefined(Language)) throw new InvalidDataException($"Unsupported application language {Language}.");
        if (TrayMenu is null) throw new InvalidDataException("Settings tray menu options are missing.");
        Profile.Validate();
        return this;
    }
}
