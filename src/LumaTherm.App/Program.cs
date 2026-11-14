namespace LumaTherm.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (commandLine is ["--verify-lighting"])
        {
            return LightingStartupCheck.Run();
        }

        var arguments = StartupActivationArguments.Resolve(
            commandLine,
            StartupActivationArguments.GetPackagedActivationKind);
        var application = new App(arguments);
        application.InitializeComponent();
        return application.Run();
    }
}
