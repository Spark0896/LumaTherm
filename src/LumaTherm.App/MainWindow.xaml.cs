using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;

namespace LumaTherm.App;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow() => InitializeComponent();

    internal IReadOnlyList<Button> IconOnlyButtons => [HomeButton, SettingsButton, AboutButton, MinimizeButton, MaximizeButton, CloseButton];

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        DashboardContent.Visibility = Visibility.Visible;
        SettingsPlaceholder.Visibility = Visibility.Collapsed;
        SetNavigationState(HomeButton, SettingsButton);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        DashboardContent.Visibility = Visibility.Collapsed;
        SettingsPlaceholder.Visibility = Visibility.Visible;
        SetNavigationState(SettingsButton, HomeButton);
    }

    private static void SetNavigationState(Button active, Button inactive)
    {
        active.Foreground = (Brush)Application.Current.Resources["ColdColorBrush"];
        active.Background = new SolidColorBrush(Color.FromRgb(0x20, 0x27, 0x2D));
        inactive.ClearValue(ForegroundProperty);
        inactive.ClearValue(BackgroundProperty);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, e);
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
