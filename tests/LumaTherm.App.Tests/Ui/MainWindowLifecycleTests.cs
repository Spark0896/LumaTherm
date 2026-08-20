using System.Windows;
using System.Windows.Threading;
using LumaTherm.App.Services;

namespace LumaTherm.App.Tests.Ui;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class MainWindowLifecycleTests(ThermalCoreStaFixture sta)
{
    [Fact]
    public void CloseAndMinimize_HideToTrayUntilExplicitExit()
    {
        sta.Run(() =>
        {
            var policy = new WindowClosePolicy(() => true);
            var window = new MainWindow { ClosePolicy = policy };
            window.Show();

            window.Close();
            Assert.False(window.IsVisible);
            Assert.True(window.IsLoaded);

            window.Show();
            window.WindowState = WindowState.Minimized;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.False(window.IsVisible);
            Assert.True(window.IsLoaded);

            policy.RequestExplicitExit();
            window.Show();
            window.Close();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.False(window.IsLoaded);
        });
    }
}
