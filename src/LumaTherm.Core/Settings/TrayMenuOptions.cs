namespace LumaTherm.Core.Settings;

public sealed record TrayMenuOptions(bool ShowTemperature, bool ShowOpenCommand, bool ShowModeToggle)
{
    public static TrayMenuOptions Default { get; } = new(true, true, true);
}
