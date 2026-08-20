using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using LumaTherm.App.ViewModels;
using LumaTherm.App.Services;

namespace LumaTherm.App;

public partial class MainWindow : System.Windows.Window
{
    private const double WorkspaceGradientAngleDegrees = 145;

    public static readonly DependencyProperty SettingsDataContextProperty = DependencyProperty.Register(
        nameof(SettingsDataContext),
        typeof(SettingsViewModel),
        typeof(MainWindow));

    public MainWindow()
    {
        ShowDashboardCommand = new RelayCommand(ShowDashboard);
        ShowSettingsCommand = new RelayCommand(ShowSettings);
        InitializeComponent();
        Closing += OnWindowClosing;
        StateChanged += OnWindowStateChanged;
    }

    public WindowClosePolicy? ClosePolicy { get; set; }

    public SettingsViewModel? SettingsDataContext
    {
        get => (SettingsViewModel?)GetValue(SettingsDataContextProperty);
        set => SetValue(SettingsDataContextProperty, value);
    }

    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }

    internal IReadOnlyList<Button> IconOnlyButtons => [HomeButton, SettingsButton, AboutButton, MinimizeButton, MaximizeButton, CloseButton];

    private void ShowDashboard()
    {
        DashboardContent.Visibility = Visibility.Visible;
        SettingsContent.Visibility = Visibility.Collapsed;
        HomeSelectionIndicator.Visibility = Visibility.Visible;
        SettingsSelectionIndicator.Visibility = Visibility.Collapsed;
        SetNavigationState(HomeButton, SettingsButton);
    }

    private void ShowSettings()
    {
        DashboardContent.Visibility = Visibility.Collapsed;
        SettingsContent.Visibility = Visibility.Visible;
        HomeSelectionIndicator.Visibility = Visibility.Collapsed;
        SettingsSelectionIndicator.Visibility = Visibility.Visible;
        SetNavigationState(SettingsButton, HomeButton);
    }

    private static void SetNavigationState(Button active, Button inactive)
    {
        active.Foreground = (Brush)Application.Current.Resources["ColdColorBrush"];
        active.Background = new SolidColorBrush(Color.FromRgb(0x20, 0x27, 0x2D));
        inactive.ClearValue(ForegroundProperty);
        inactive.ClearValue(BackgroundProperty);
        System.Windows.Automation.AutomationProperties.SetItemStatus(active, "Выбрано");
        System.Windows.Automation.AutomationProperties.SetItemStatus(inactive, string.Empty);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnWindowClosing(object? sender, CancelEventArgs args)
    {
        if (ClosePolicy?.Decide(WindowCloseReason.Close) == WindowCloseDecision.HideAndCancel)
        {
            args.Cancel = true;
            Hide();
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs args)
    {
        if (WindowState == WindowState.Minimized &&
            ClosePolicy?.Decide(WindowCloseReason.Minimize) == WindowCloseDecision.HideAndCancel)
        {
            Hide();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, e);
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void WorkspaceSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0 || WorkspaceSurface.Background is not LinearGradientBrush brush)
        {
            return;
        }

        var radians = WorkspaceGradientAngleDegrees * Math.PI / 180;
        var directionX = Math.Sin(radians);
        var directionY = -Math.Cos(radians);
        var lineLength = Math.Abs(e.NewSize.Width * directionX) + Math.Abs(e.NewSize.Height * directionY);
        var halfRelativeX = directionX * lineLength / (2 * e.NewSize.Width);
        var halfRelativeY = directionY * lineLength / (2 * e.NewSize.Height);

        brush.StartPoint = new System.Windows.Point(0.5 - halfRelativeX, 0.5 - halfRelativeY);
        brush.EndPoint = new System.Windows.Point(0.5 + halfRelativeX, 0.5 + halfRelativeY);
    }
}
