using System.Windows;
using WpfApplication = System.Windows.Application;
using WpfWindow = System.Windows.Window;

namespace LumaTherm.App.Services;

public sealed class WpfTrayWindow(WpfWindow window) : ITrayWindow
{
    public bool IsVisible => window.IsVisible;
    public bool IsMinimized => window.WindowState == WindowState.Minimized;
    public void Show() => window.Show();
    public void Restore() => window.WindowState = WindowState.Normal;
    public void Activate() => window.Activate();
    public void BringToFront()
    {
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}

public sealed class WpfTrayApplication : ITrayApplication
{
    private readonly WpfApplication _application;

    public WpfTrayApplication(WpfApplication? application = null)
    {
        _application = application ?? WpfApplication.Current;
    }

    public void RequestShutdown() => _application.Dispatcher.BeginInvoke((Action)_application.Shutdown);
}
