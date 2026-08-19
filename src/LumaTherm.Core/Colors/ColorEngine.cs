namespace LumaTherm.Core.Colors;

public enum ThermalRange
{
    Cold,
    Warm,
    Hot,
}

public sealed class ColorEngine
{
    private double _smoothedTemperature;

    public ColorEngine(ThermalProfile profile, double initialTemperature)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile.Validate();
        _smoothedTemperature = initialTemperature;
    }

    public ThermalProfile Profile { get; }

    public ThermalRange Classify(double temperature) => temperature <= Profile.ColdTemperature
        ? ThermalRange.Cold
        : temperature >= Profile.HotTemperature
            ? ThermalRange.Hot
            : ThermalRange.Warm;

    public RgbColor Map(double temperature)
    {
        var clamped = Math.Clamp(temperature, Profile.ColdTemperature, Profile.HotTemperature);
        return clamped <= Profile.WarmTemperature
            ? Interpolate(Profile.ColdColor, Profile.WarmColor, (clamped - Profile.ColdTemperature) / (Profile.WarmTemperature - Profile.ColdTemperature))
            : Interpolate(Profile.WarmColor, Profile.HotColor, (clamped - Profile.WarmTemperature) / (Profile.HotTemperature - Profile.WarmTemperature));
    }

    public RgbColor Step(double temperature, TimeSpan elapsed)
    {
        var alpha = 1 - Math.Exp(-elapsed.TotalSeconds / Profile.SmoothingSeconds);
        _smoothedTemperature += (temperature - _smoothedTemperature) * alpha;
        return Map(_smoothedTemperature);
    }

    private static RgbColor Interpolate(RgbColor start, RgbColor end, double amount)
    {
        var first = RgbToHsv(start);
        var second = RgbToHsv(end);
        var hueDelta = ((second.Hue - first.Hue + 540) % 360) - 180;
        return HsvToRgb(new Hsv(
            (first.Hue + (amount * hueDelta) + 360) % 360,
            first.Saturation + ((second.Saturation - first.Saturation) * amount),
            first.Value + ((second.Value - first.Value) * amount)));
    }

    private static Hsv RgbToHsv(RgbColor color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;

        var hue = delta == 0
            ? 0
            : maximum == red
                ? 60 * (((green - blue) / delta) % 6)
                : maximum == green
                    ? 60 * (((blue - red) / delta) + 2)
                    : 60 * (((red - green) / delta) + 4);
        if (hue < 0)
        {
            hue += 360;
        }

        return new Hsv(hue, maximum == 0 ? 0 : delta / maximum, maximum);
    }

    private static RgbColor HsvToRgb(Hsv color)
    {
        var chroma = color.Value * color.Saturation;
        var secondary = chroma * (1 - Math.Abs(((color.Hue / 60) % 2) - 1));
        var match = color.Value - chroma;
        var (red, green, blue) = color.Hue switch
        {
            < 60 => (chroma, secondary, 0d),
            < 120 => (secondary, chroma, 0d),
            < 180 => (0d, chroma, secondary),
            < 240 => (0d, secondary, chroma),
            < 300 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary),
        };

        return new RgbColor(
            ToByte(red + match),
            ToByte(green + match),
            ToByte(blue + match));
    }

    private static byte ToByte(double channel) => (byte)Math.Round(channel * 255, MidpointRounding.AwayFromZero);

    private readonly record struct Hsv(double Hue, double Saturation, double Value);
}
