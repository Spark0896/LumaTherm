using LumaTherm.App.Services;

namespace LumaTherm.App.Tests.Services;

public sealed class WindowClosePolicyTests
{
    [Fact]
    public void OrdinaryCloseAndMinimize_HideWhenCloseToTrayEnabled()
    {
        var policy = new WindowClosePolicy(() => true);

        Assert.Equal(WindowCloseDecision.HideAndCancel, policy.Decide(WindowCloseReason.Close));
        Assert.Equal(WindowCloseDecision.HideAndCancel, policy.Decide(WindowCloseReason.Minimize));
    }

    [Fact]
    public void ExplicitExit_AlwaysAllowsClose()
    {
        var policy = new WindowClosePolicy(() => true);

        policy.RequestExplicitExit();

        Assert.Equal(WindowCloseDecision.AllowClose, policy.Decide(WindowCloseReason.Close));
        Assert.True(policy.IsExplicitExitRequested);
    }

    [Fact]
    public void DisabledCloseToTray_AllowsNormalCloseAndMinimize()
    {
        var policy = new WindowClosePolicy(() => false);

        Assert.Equal(WindowCloseDecision.AllowClose, policy.Decide(WindowCloseReason.Close));
        Assert.Equal(WindowCloseDecision.AllowClose, policy.Decide(WindowCloseReason.Minimize));
    }
}
