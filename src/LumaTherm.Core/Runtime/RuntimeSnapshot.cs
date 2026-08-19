using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Sensors;

namespace LumaTherm.Core.Runtime;

public sealed record RuntimeSnapshot(
    RuntimeStatus Status,
    TemperatureReading? Temperature,
    RgbColor? Color,
    ThermalRange? Range,
    LightingDeviceInfo? LightingDevice,
    string? Message,
    DateTimeOffset Timestamp,
    bool IsModeEnabled = false);
