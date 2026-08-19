namespace LumaTherm.Core.Runtime;

public enum RuntimeStatus
{
    Disabled,
    Connecting,
    Active,
    HoldingLastColor,
    SensorUnavailable,
    LightingUnavailable,
    Suspended,
    Faulted,
}
