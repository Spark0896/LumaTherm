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
        35, new RgbColor(0x50, 0xC8, 0xFF),
        65, new RgbColor(0xFF, 0xC6, 0x4A),
        85, new RgbColor(0xFF, 0x56, 0x5D),
        0.8);

    public ThermalProfile Validate()
    {
        if (ColdTemperature is < 0 or > 120 || WarmTemperature is < 0 or > 120 || HotTemperature is < 0 or > 120)
        {
            throw new ArgumentException("Temperatures must be between 0 and 120 °C.");
        }

        if (WarmTemperature - ColdTemperature < 1 || HotTemperature - WarmTemperature < 1)
        {
            throw new ArgumentException("Expected ColdTemperature < WarmTemperature < HotTemperature with at least 1 °C between points.");
        }

        if (SmoothingSeconds is < 0.1 or > 5.0)
        {
            throw new ArgumentException("SmoothingSeconds must be between 0.1 and 5.0.");
        }

        return this;
    }
}
