using LumaTherm.Core.Colors;

namespace LumaTherm.Core.Lighting;

public sealed class LightingCommandGate(TimeSpan minimumInterval)
{
    private DateTimeOffset? _lastSentAt;
    private RgbColor? _lastColor;

    public bool ShouldSend(RgbColor color, DateTimeOffset now)
    {
        if (_lastColor == color)
        {
            return false;
        }

        if (_lastSentAt is { } sent && now - sent < minimumInterval)
        {
            return false;
        }

        _lastColor = color;
        _lastSentAt = now;
        return true;
    }

    public void Reset()
    {
        _lastColor = null;
        _lastSentAt = null;
    }
}
