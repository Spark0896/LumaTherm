namespace LumaTherm.Core.Lighting;

public sealed class LightingControlUnavailableException : InvalidOperationException
{
    public LightingControlUnavailableException()
        : base("Windows has temporarily assigned lighting control to another application.")
    {
    }
}
