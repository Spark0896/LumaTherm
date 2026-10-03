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


    public static readonly DependencyProperty SettingsDataContextProperty = DependencyProperty.Register(
        nameof(SettingsDataContext),
        typeof(SettingsViewModel),
        typeof(MainWindow));

    public static readonly DependencyProperty AboutDataContextProperty = DependencyProperty.Register(
        nameof(AboutDataContext),
        typeof(AboutViewModel),
        typeof(MainWindow));

    public MainWindow()
    {
        ShowDashboardCommand = new RelayCommand(ShowDashboard);
        ShowSettingsCommand = new RelayCommand(ShowSettings);
        ShowAboutCommand = new RelayCommand(ShowAbout);
        OpenLightingTestCommand = new RelayCommand(() =>
        {
            if (!CanStartLightingTest) { ShowSettings(); return; }
            SettingsDataContext?.OpenLightingTestCommand.Execute(null);
        });
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

    public AboutViewModel? AboutDataContext
    {
        get => (AboutViewModel?)GetValue(AboutDataContextProperty);
        set => SetValue(AboutDataContextProperty, value);
    }

    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand ShowAboutCommand { get; }
    public RelayCommand OpenLightingTestCommand { get; }
    internal bool CanStartLightingTest => !SettingsContent.HasInvalidNumericInput;

    internal IReadOnlyList<Button> IconOnlyButtons => [HomeButton, SettingsButton, AboutButton, MinimizeButton, MaximizeButton, CloseButton];

    private void ShowDashboard()
    {
        DashboardContent.Visibility = Visibility.Visible;
        SettingsContent.Visibility = Visibility.Collapsed;
        AboutContent.Visibility = Visibility.Collapsed;
        HomeSelectionIndicator.Visibility = Visibility.Visible;
        SettingsSelectionIndicator.Visibility = Visibility.Collapsed;
        AboutSelectionIndicator.Visibility = Visibility.Collapsed;
        SetNavigationState(HomeButton, SettingsButton, AboutButton);
    }

    private void ShowSettings()
    {
        DashboardContent.Visibility = Visibility.Collapsed;
        SettingsContent.Visibility = Visibility.Visible;
        AboutContent.Visibility = Visibility.Collapsed;
        HomeSelectionIndicator.Visibility = Visibility.Collapsed;
        SettingsSelectionIndicator.Visibility = Visibility.Visible;
        AboutSelectionIndicator.Visibility = Visibility.Collapsed;
        SetNavigationState(SettingsButton, HomeButton, AboutButton);
    }

    private void ShowAbout()
    {
        DashboardContent.Visibility = Visibility.Collapsed;
        SettingsContent.Visibility = Visibility.Collapsed;
        AboutContent.Visibility = Visibility.Visible;
        HomeSelectionIndicator.Visibility = Visibility.Collapsed;
        SettingsSelectionIndicator.Visibility = Visibility.Collapsed;
        AboutSelectionIndicator.Visibility = Visibility.Visible;
        SetNavigationState(AboutButton, HomeButton, SettingsButton);
    }

    private static void SetNavigationState(Button active, params Button[] inactive)
    {
        active.Foreground = (Brush)Application.Current.Resources["ColdColorBrush"];
        active.Background = new SolidColorBrush(Color.FromRgb(0x29, 0x44, 0x59));
        active.SetResourceReference(System.Windows.Automation.AutomationProperties.ItemStatusProperty, "Accessibility.Selected");
        foreach (var button in inactive)
        {
            button.ClearValue(ForegroundProperty);
            button.ClearValue(BackgroundProperty);
            button.ClearValue(System.Windows.Automation.AutomationProperties.ItemStatusProperty);
        }
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

}
