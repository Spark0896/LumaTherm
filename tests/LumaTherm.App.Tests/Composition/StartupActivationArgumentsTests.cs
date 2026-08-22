using Windows.ApplicationModel.Activation;

namespace LumaTherm.App.Tests.Composition;

public sealed class StartupActivationArgumentsTests
{
    [Fact]
    public void StartupTaskActivationAddsHiddenStartupArgument()
    {
        var arguments = StartupActivationArguments.Resolve(["--existing"], () => ActivationKind.StartupTask);

        Assert.Equal(["--existing", "--autostart"], arguments);
    }

    [Fact]
    public void ManualLaunchDoesNotHideTheWindow()
    {
        var arguments = StartupActivationArguments.Resolve(["--existing"], () => ActivationKind.Launch);

        Assert.Equal(["--existing"], arguments);
    }

    [Fact]
    public void ExplicitPortableAutostartArgumentIsNotDuplicated()
    {
        var arguments = StartupActivationArguments.Resolve(["--autostart"], () => ActivationKind.StartupTask);

        Assert.Equal(["--autostart"], arguments);
    }

    [Fact]
    public void UnpackagedActivationProbeFailureKeepsCommandLineArguments()
    {
        var arguments = StartupActivationArguments.Resolve(
            ["--existing"],
            () => throw new InvalidOperationException("No package identity."));

        Assert.Equal(["--existing"], arguments);
    }
}
