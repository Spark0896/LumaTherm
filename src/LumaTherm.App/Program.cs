namespace LumaTherm.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var arguments = StartupActivationArguments.Resolve(
            Environment.GetCommandLineArgs().Skip(1),
            StartupActivationArguments.GetPackagedActivationKind);
        var application = new App(arguments);
        application.InitializeComponent();
        application.Run();
    }
}
