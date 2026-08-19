using LumaTherm.Infrastructure.Lighting;

namespace LumaTherm.Infrastructure.Tests.Lighting;

public sealed class LampArrayAvailabilityStateTests
{
    [Fact]
    public void ApplySnapshot_DoesNotOverwriteANewerRemovalTombstone()
    {
        var state = new LampArrayAvailabilityState();
        var snapshotGeneration = state.CaptureGeneration();
        state.RecordWatcherUpdate("device-1", false);

        state.ApplySnapshot("device-1", true, snapshotGeneration);

        Assert.False(state.IsAvailable("device-1"));
    }

    [Fact]
    public void ApplySnapshot_AllowsADeviceSeenByAnEnumerationStartedAfterRemoval()
    {
        var state = new LampArrayAvailabilityState();
        state.RecordWatcherUpdate("device-1", false);
        var snapshotGeneration = state.CaptureGeneration();

        state.ApplySnapshot("device-1", true, snapshotGeneration);

        Assert.True(state.IsAvailable("device-1"));
    }
}
