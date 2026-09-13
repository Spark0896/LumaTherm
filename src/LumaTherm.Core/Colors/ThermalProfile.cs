using System.Text.Json.Serialization;

namespace LumaTherm.Core.Colors;

public sealed record ThermalProfile : IEquatable<ThermalProfile>
{
    private ThermalPoint[] _points;

    public ThermalProfile(double coldTemperature, RgbColor coldColor, double warmTemperature, RgbColor warmColor, double hotTemperature, RgbColor hotColor, double smoothingSeconds)
    {
        _points = [new(coldTemperature, coldColor), new(warmTemperature, warmColor), new(hotTemperature, hotColor)];
        SmoothingSeconds = smoothingSeconds;
    }

    [JsonConstructor]
    private ThermalProfile(IReadOnlyList<ThermalPoint> points, double smoothingSeconds)
    {
        _points = points?.ToArray() ?? [];
        SmoothingSeconds = smoothingSeconds;
    }

    public static ThermalProfile Default { get; } = new(35, new RgbColor(0x00, 0x6B, 0xFF), 65, new RgbColor(0xD0, 0x00, 0xFF), 85, new RgbColor(0xFF, 0x00, 0x00), 0.8);
    public IReadOnlyList<ThermalPoint> Points => Array.AsReadOnly(_points);
    public double SmoothingSeconds { get; init; }
    public ThermalPoint ColdPoint => _points[0];
    public ThermalPoint HotPoint => _points[^1];
    public double ColdTemperature { get => ColdPoint.Temperature; init => _points = ReplacePoint(0, temperature: value); }
    public RgbColor ColdColor { get => ColdPoint.Color; init => _points = ReplacePoint(0, color: value); }
    public double WarmTemperature { get => _points[1].Temperature; init => _points = ReplacePoint(1, temperature: value); }
    public RgbColor WarmColor { get => _points[1].Color; init => _points = ReplacePoint(1, color: value); }
    public double HotTemperature { get => HotPoint.Temperature; init => _points = ReplacePoint(_points.Length - 1, temperature: value); }
    public RgbColor HotColor { get => HotPoint.Color; init => _points = ReplacePoint(_points.Length - 1, color: value); }

    public static ThermalProfile Create(IEnumerable<ThermalPoint> points, double smoothingSeconds)
    {
        ArgumentNullException.ThrowIfNull(points);
        return new ThermalProfile(points.ToArray(), smoothingSeconds).Validate();
    }

    public ThermalProfile Validate()
    {
        if (_points.Length < 2) throw new ArgumentException("At least two thermal points are required.");
        foreach (var point in _points)
            if (!double.IsFinite(point.Temperature) || point.Temperature is < 0 or > 120)
                throw new ArgumentException("Temperatures must be between 0 and 120 °C.");
        for (var index = 1; index < _points.Length; index++)
            if (_points[index].Temperature - _points[index - 1].Temperature < 1)
                throw new ArgumentException("Expected ColdTemperature < WarmTemperature < HotTemperature with at least 1 °C between points.");
        if (!double.IsFinite(SmoothingSeconds) || SmoothingSeconds is < 0.1 or > 5.0)
            throw new ArgumentException("SmoothingSeconds must be between 0.1 and 5.0.");
        return this;
    }

    public bool ContentEquals(ThermalProfile? other) => other is not null && SmoothingSeconds.Equals(other.SmoothingSeconds) && _points.SequenceEqual(other._points);
    public bool Equals(ThermalProfile? other) => ContentEquals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SmoothingSeconds);
        foreach (var point in _points) hash.Add(point);
        return hash.ToHashCode();
    }

    private ThermalPoint[] ReplacePoint(int index, double? temperature = null, RgbColor? color = null)
    {
        var points = (ThermalPoint[])_points.Clone();
        var current = points[index];
        points[index] = new ThermalPoint(temperature ?? current.Temperature, color ?? current.Color);
        return points;
    }
}
