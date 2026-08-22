namespace LumaTherm.Core.Colors;

public sealed record ThermalProfile(
    double ColdTemperature,
    RgbColor ColdColor,
    double WarmTemperature,
    RgbColor WarmColor,
    double HotTemperature,
    RgbColor HotColor,
    double SmoothingSeconds)
{
    public static ThermalProfile Default { get; } = new(
        35, new RgbColor(0x00, 0x8C, 0xFF),
        65, new RgbColor(0xFF, 0xD8, 0x00),
        85, new RgbColor(0xFF, 0x18, 0x00),
        0.8);

    public ThermalProfile Validate()
    {
        if (!double.IsFinite(ColdTemperature) || !double.IsFinite(WarmTemperature) || !double.IsFinite(HotTemperature)
            || ColdTemperature is < 0 or > 120 || WarmTemperature is < 0 or > 120 || HotTemperature is < 0 or > 120)
        {
            throw new ArgumentException("Temperatures must be between 0 and 120 °C.");
        }

        if (WarmTemperature - ColdTemperature < 1 || HotTemperature - WarmTemperature < 1)
        {
            throw new ArgumentException("Expected ColdTemperature < WarmTemperature < HotTemperature with at least 1 °C between points.");
        }

        if (!double.IsFinite(SmoothingSeconds) || SmoothingSeconds is < 0.1 or > 5.0)
        {
            throw new ArgumentException("SmoothingSeconds must be between 0.1 and 5.0.");
        }

        return this;
    }
}
