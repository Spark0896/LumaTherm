namespace LumaTherm.Core.Sensors;

public sealed record TemperatureReading(double Celsius, string SourceName, string DeviceName, DateTimeOffset Timestamp);
