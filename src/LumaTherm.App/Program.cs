namespace LumaTherm.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var arguments = StartupActivationArguments.Resolve(
            commandLine,
            StartupActivationArguments.GetPackagedActivationKind);
        var application = new App(arguments);
        application.InitializeComponent();
        application.Run();
    }
}
