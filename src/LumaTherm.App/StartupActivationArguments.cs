using Windows.ApplicationModel.Activation;

namespace LumaTherm.App;

internal static class StartupActivationArguments
{
    private const string AutostartArgument = "--autostart";

    public static string[] Resolve(IEnumerable<string> commandLineArguments, Func<ActivationKind?> getActivationKind)
    {
        ArgumentNullException.ThrowIfNull(commandLineArguments);
        ArgumentNullException.ThrowIfNull(getActivationKind);
        var arguments = commandLineArguments.ToList();
        if (arguments.Any(argument => string.Equals(argument, AutostartArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return arguments.ToArray();
        }

        try
        {
            if (getActivationKind() == ActivationKind.StartupTask)
            {
                arguments.Add(AutostartArgument);
            }
        }
        catch (Exception)
        {
            // Unpackaged launches do not expose packaged activation data.
        }

        return arguments.ToArray();
    }

    public static ActivationKind? GetPackagedActivationKind() =>
        Windows.ApplicationModel.AppInstance.GetActivatedEventArgs()?.Kind;
}
