using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;

namespace LumaTherm.Core.Tests.Lighting;

public sealed class LightingCommandGateTests
{
    [Fact]
    public void ShouldSend_SuppressesDuplicatesAndSub100MillisecondUpdates()
    {
        var gate = new LightingCommandGate(TimeSpan.FromMilliseconds(100));
        var start = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Assert.True(gate.ShouldSend(new RgbColor(1, 2, 3), start));
        Assert.False(gate.ShouldSend(new RgbColor(1, 2, 3), start.AddSeconds(1)));
        Assert.False(gate.ShouldSend(new RgbColor(2, 3, 4), start.AddMilliseconds(99)));
        Assert.True(gate.ShouldSend(new RgbColor(2, 3, 4), start.AddMilliseconds(100)));
    }

    [Fact]
    public void Reset_AllowsTheSameColorImmediately()
    {
        var gate = new LightingCommandGate(TimeSpan.FromMilliseconds(100));
        var start = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Assert.True(gate.ShouldSend(new RgbColor(1, 2, 3), start));

        gate.Reset();

        Assert.True(gate.ShouldSend(new RgbColor(1, 2, 3), start.AddMilliseconds(1)));
    }
}
