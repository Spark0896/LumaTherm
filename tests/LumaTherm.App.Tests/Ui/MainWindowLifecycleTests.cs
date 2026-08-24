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

    [Fact]
    public void MinimizeAndDeactivate_DoNotCrossTheWindowOwnershipBoundary()
    {
        sta.Run(() =>
        {
            var policy = new WindowClosePolicy(() => true);
            var ownershipReleases = 0;
            var window = new MainWindow { ClosePolicy = policy };
            window.Closed += (_, _) => ownershipReleases++;
            window.Show();

            window.WindowState = WindowState.Minimized;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            SimulateDeactivated(window);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            Assert.False(window.IsVisible);
            Assert.True(window.IsLoaded);
            Assert.Equal(0, ownershipReleases);

            policy.RequestExplicitExit();
            window.Show();
            window.Close();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.Equal(1, ownershipReleases);
        });
    }

    private static void SimulateDeactivated(Window window) =>
        typeof(Window).GetMethod("OnDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, [EventArgs.Empty]);
}
