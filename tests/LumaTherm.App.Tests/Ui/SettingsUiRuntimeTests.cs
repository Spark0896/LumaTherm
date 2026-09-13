using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using LumaTherm.App.Controls;
using LumaTherm.App.Services;
using LumaTherm.App.ViewModels;
using LumaTherm.App.Views;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;

namespace LumaTherm.App.Tests.Ui;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class SettingsUiRuntimeTests
{
    private readonly ThermalCoreStaFixture _sta;

    public SettingsUiRuntimeTests(ThermalCoreStaFixture sta) => _sta = sta;

    [Fact]
    public void SettingsView_UsesInteractiveProfileEditorAndNewPreferenceBindings() => _sta.Run(() =>
    {
        using var vm = CreateViewModel();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);

        var editor = Assert.IsType<ThermalProfileEditor>(view.FindName("ProfileEditorControl"));
        Assert.Same(vm.ProfileEditor, editor.DataContext);
        Assert.Same(vm.PickSelectedColorCommand, Button(view, "SelectedPointColorButton").Command);
        Assert.Same(vm.OpenLightingTestCommand, Button(view, "OpenLightingTestButton").Command);
        Assert.Same(vm.SaveCommand, Button(view, "SaveButton").Command);
        Assert.Same(vm.ResetDefaultsCommand, Button(view, "ResetButton").Command);
        var language = Assert.IsType<ComboBox>(view.FindName("LanguageSelector"));
        Assert.Equal(Color.FromRgb(0x1A, 0x21, 0x28), Assert.IsType<SolidColorBrush>(language.Background).Color);
        Assert.Equal(Color.FromRgb(0xF4, 0xF7, 0xFA), Assert.IsType<SolidColorBrush>(language.Foreground).Color);
        language.ApplyTemplate();
        var comboChrome = Assert.IsType<Border>(language.Template.FindName("ComboChrome", language));
        Assert.Equal(Color.FromRgb(0x1A, 0x21, 0x28), Assert.IsType<SolidColorBrush>(comboChrome.Background).Color);
        Assert.NotNull(Assert.IsType<Path>(language.Template.FindName("DropDownArrow", language)).Data);
        var swatch = Button(view, "SelectedPointColorButton");
        Assert.Equal(new Thickness(2), swatch.BorderThickness);
        Assert.Equal(Color.FromRgb(0xAE, 0xBA, 0xC4), Assert.IsType<SolidColorBrush>(swatch.BorderBrush).Color);
        AssertBinding<Slider>(view, "SmoothingSlider", Slider.ValueProperty, nameof(SettingsViewModel.SmoothingSeconds));
        AssertBinding<TextBlock>(view, "ValidationText", TextBlock.TextProperty, nameof(SettingsViewModel.ValidationMessage));
        AssertBinding<ComboBox>(view, "LanguageSelector", ComboBox.SelectedValueProperty, nameof(SettingsViewModel.SelectedLanguage));
        AssertBinding<CheckBox>(view, "TrayTemperatureToggle", System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, nameof(SettingsViewModel.ShowTrayTemperature));
        AssertBinding<CheckBox>(view, "TrayOpenToggle", System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, nameof(SettingsViewModel.ShowTrayOpen));
        AssertBinding<CheckBox>(view, "TrayModeToggle", System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, nameof(SettingsViewModel.ShowTrayModeToggle));
        Assert.Null(view.FindName("ColdTemperatureSlider"));
        Assert.Null(view.FindName("WarmTemperatureSlider"));
        Assert.Null(view.FindName("HotTemperatureSlider"));
    });

    [Fact]
    public void SettingsView_SelectedPointEditorAndSmoothingUseExactLimits() => _sta.Run(() =>
    {
        using var vm = CreateViewModel();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);
        var smoothing = Assert.IsType<Slider>(view.FindName("SmoothingSlider"));
        var temperature = Assert.IsType<TextBox>(view.FindName("SelectedPointTemperatureEditor"));

        Assert.Equal(0.1, smoothing.Minimum);
        Assert.Equal(5, smoothing.Maximum);
        var binding = BindingOperations.GetBindingExpression(temperature, TextBox.TextProperty);
        Assert.NotNull(binding);
        Assert.Equal(UpdateSourceTrigger.LostFocus, binding.ParentBinding.UpdateSourceTrigger);
        temperature.Text = "40";
        binding.UpdateSource();
        Assert.Equal(40, vm.ProfileEditor.Points[0].Temperature);
    });

    [Fact]
    public void SettingsView_UsesActualTemperaturePreviewAndExplicitHeadingForeground() => _sta.Run(() =>
    {
        var runtime = new FakeRuntime();
        using var vm = CreateViewModel(runtime);
        runtime.Publish(Snapshot(51.4));
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);
        var heading = Assert.IsType<TextBlock>(view.FindName("ColorScaleHeading"));
        var preview = Assert.IsType<TextBlock>(view.FindName("TrayTemperaturePreviewText"));

        Assert.Same(Application.Current.Resources["PrimaryTextBrush"], heading.Foreground);
        Assert.Equal("51°C", preview.Text);
        Assert.DoesNotContain("68°C", FlattenText(view), StringComparison.Ordinal);
    });

    [Fact]
    public void SettingsView_TrayPreviewHidesOptionalEntriesIndividuallyButNeverExit() => _sta.Run(() =>
    {
        using var vm = CreateViewModel();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);
        var temperature = Assert.IsType<Grid>(view.FindName("TrayTemperaturePreviewEntry"));
        var open = Assert.IsType<TextBlock>(view.FindName("TrayOpenPreviewEntry"));
        var mode = Assert.IsType<TextBlock>(view.FindName("TrayModePreviewEntry"));
        var exit = Assert.IsType<TextBlock>(view.FindName("TrayExitPreviewEntry"));

        vm.ShowTrayTemperature = false;
        vm.ShowTrayOpen = false;
        vm.ShowTrayModeToggle = false;
        view.UpdateLayout();

        Assert.Equal(Visibility.Collapsed, temperature.Visibility);
        Assert.Equal(Visibility.Collapsed, open.Visibility);
        Assert.Equal(Visibility.Collapsed, mode.Visibility);
        Assert.Equal(Visibility.Visible, exit.Visibility);
        Assert.Null(BindingOperations.GetBindingExpression(exit, UIElement.VisibilityProperty));
    });

    [Fact]
    public void SettingsView_TrayPreviewActionsRemainReadableOnTheDarkCard() => _sta.Run(() =>
    {
        using var vm = CreateViewModel();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);
        var expected = Assert.IsType<SolidColorBrush>(Application.Current.Resources["PrimaryTextBrush"]).Color;

        foreach (var name in new[] { "TrayOpenPreviewEntry", "TrayModePreviewEntry", "TrayExitPreviewEntry" })
        {
            var entry = Assert.IsType<TextBlock>(view.FindName(name));
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(entry.Foreground).Color);
        }
    });

    [Fact]
    public void SettingsView_LightingTestActionLivesInsideTheColorScaleCard() => _sta.Run(() =>
    {
        using var vm = CreateViewModel();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);
        var colorScale = Assert.IsType<Border>(view.FindName("ColorScalePanel"));
        var hardware = Assert.IsType<Border>(view.FindName("HardwarePanel"));
        var button = Assert.IsType<Button>(view.FindName("OpenLightingTestButton"));

        Assert.True(IsVisualAncestor(colorScale, button));
        Assert.False(IsVisualAncestor(hardware, button));
    });

    [Fact]
    public void MainWindow_NavigationCommandsSwitchCompiledContentAndSelectedAccessibilityState() => _sta.Run(() =>
    {
        using var dashboard = new MainViewModel(new FakeRuntime());
        using var settings = CreateViewModel();
        var shell = new MainWindow { DataContext = dashboard, SettingsDataContext = settings };
        try
        {
            Arrange(Assert.IsType<Border>(shell.Content), 1180, 720);
            var home = Assert.IsType<Button>(shell.FindName("HomeButton"));
            var settingsButton = Assert.IsType<Button>(shell.FindName("SettingsButton"));
            var dashboardContent = Assert.IsType<DashboardView>(shell.FindName("DashboardContent"));
            var settingsContent = Assert.IsType<SettingsView>(shell.FindName("SettingsContent"));

            settingsButton.Command.Execute(null);

            Assert.Equal(Visibility.Collapsed, dashboardContent.Visibility);
            Assert.Equal(Visibility.Visible, settingsContent.Visibility);
            Assert.Same(settings, settingsContent.DataContext);
            Assert.Equal("", AutomationProperties.GetItemStatus(home));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetItemStatus(settingsButton)));
        }
        finally
        {
            shell.Close();
        }
    });

    [Theory]
    [InlineData(979, true)]
    [InlineData(980, false)]
    public void SettingsView_NarrowBreakpointStacksWithoutClippingPrimaryEditors(double width, bool compact) => _sta.Run(() =>
    {
        using var vm = CreateViewModel();
        var view = Arrange(new SettingsView { DataContext = vm }, width, 552);

        Assert.Equal(compact, view.IsCompactLayout);
        Assert.Equal(compact ? 1 : 2, view.ActiveColumnCount);
        foreach (var name in new[] { "ProfileEditorControl", "SelectedPointTemperatureEditor", "SmoothingEditor", "ResetButton", "SaveButton" })
        {
            var element = Assert.IsAssignableFrom<FrameworkElement>(view.FindName(name));
            Assert.True(element.ActualWidth > 0, $"{name} has no width.");
            Assert.True(element.ActualHeight > 0, $"{name} has no height.");
            var bounds = element.TransformToAncestor(view).TransformBounds(new Rect(element.RenderSize));
            Assert.True(bounds.Left >= -0.5, $"{name} starts outside the client: {bounds}.");
            Assert.True(bounds.Right <= width + 0.5, $"{name} clips horizontally: {bounds}.");
        }

        if (compact)
        {
            Assert.True(view.SettingsScroller.ScrollableHeight > 0);
        }
    });

    [Fact]
    public void ColorPickerService_UsesActiveWpfWindowAsNativeDialogOwner() => _sta.Run(() =>
    {
        var previousMainWindow = Application.Current.MainWindow;
        var owner = new Window { Width = 240, Height = 160, ShowInTaskbar = false };
        try
        {
            Application.Current.MainWindow = owner;
            owner.Show();
            nint capturedHandle = 0;
            var current = new RgbColor(10, 20, 30);
            var service = new ColorPickerService((color, ownerHandle) =>
            {
                Assert.Equal(current, color);
                capturedHandle = ownerHandle;
                return new RgbColor(1, 2, 3);
            });

            Assert.Equal(new RgbColor(1, 2, 3), service.Pick(current));
            Assert.NotEqual(0, capturedHandle);
        }
        finally
        {
            owner.Close();
            Application.Current.MainWindow = previousMainWindow;
        }
    });

    [Fact]
    public void SettingsView_ValidationAndEveryNewEditorExposeAccessibleNames() => _sta.Run(() =>
    {
        using var vm = CreateViewModel();
        vm.SmoothingSeconds = 0;
        vm.SaveCommand.ExecuteAsync().GetAwaiter().GetResult();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);
        var validation = Assert.IsType<TextBlock>(view.FindName("ValidationText"));

        Assert.Equal("Сглаживание должно быть от 0,1 до 5,0 секунд.", validation.Text);
        Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(validation));
        foreach (var name in new[]
        {
            "SelectedPointColorButton", "SelectedPointTemperatureEditor", "SmoothingSlider", "SmoothingEditor",
            "LanguageSelector", "AutostartToggle", "MinimizeToTrayToggle", "NotificationsToggle",
            "TrayTemperatureToggle", "TrayOpenToggle", "TrayModeToggle", "OpenLightingTestButton", "ResetButton", "SaveButton",
        })
        {
            var element = Assert.IsAssignableFrom<FrameworkElement>(view.FindName(name));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(element)), $"{name} has no accessible name.");
            Assert.True(element.Focusable, $"{name} is not keyboard reachable.");
        }
    });

    [Fact]
    public void SettingsView_DeviceSelectorRendersMultipleAndUnavailableStableIds() => _sta.Run(() =>
    {
        var saved = AppSettings.Default with { PreferredLightingDeviceId = "lamp-missing" };
        using var vm = new SettingsViewModel(
            new FakeRuntime(), new FakeStartupService(), saved, new FakePicker(),
            new FakeDiscovery(new LightingDeviceInfo("lamp-a", "Desk Lamp", 8, true)));
        vm.DiscoverLightingDevicesCommand.ExecuteAsync().GetAwaiter().GetResult();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);
        var selector = Assert.IsType<ComboBox>(view.FindName("LightingDeviceSelector"));

        Assert.Equal(Visibility.Visible, selector.Visibility);
        Assert.Equal(2, selector.Items.Count);
        Assert.Equal("lamp-missing", selector.SelectedValue);
    });

    [Fact]
    public void SettingsView_NewControlsUseCompiledInteractionStates() => _sta.Run(() =>
    {
        using var vm = CreateViewModel();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);
        var toggle = Assert.IsType<CheckBox>(view.FindName("AutostartToggle"));
        var editor = Assert.IsType<TextBox>(view.FindName("SelectedPointTemperatureEditor"));
        var reset = Assert.IsType<Button>(view.FindName("ResetButton"));
        var save = Assert.IsType<Button>(view.FindName("SaveButton"));
        var openTest = Assert.IsType<Button>(view.FindName("OpenLightingTestButton"));
        Assert.Same(Application.Current.Resources["ActionButtonStyle"], reset.Style);
        Assert.Same(Application.Current.Resources["ActionButtonStyle"], openTest.Style);
        Assert.Same(Application.Current.Resources["PrimaryActionButtonStyle"], save.Style);
        toggle.ApplyTemplate();
        editor.ApplyTemplate();
        reset.ApplyTemplate();
        save.ApplyTemplate();

        var switchTrack = Assert.IsType<Border>(toggle.Template.FindName("SwitchTrack", toggle));
        var switchThumb = Assert.IsType<Ellipse>(toggle.Template.FindName("SwitchThumb", toggle));
        toggle.IsChecked = true;
        toggle.UpdateLayout();
        Assert.Equal(Color.FromRgb(0x55, 0xCB, 0xFF), Assert.IsType<SolidColorBrush>(switchTrack.Background).Color);
        Assert.Equal(HorizontalAlignment.Right, switchThumb.HorizontalAlignment);
        Assert.Equal(new CornerRadius(6), Assert.IsType<Border>(editor.Template.FindName("EditorChrome", editor)).CornerRadius);
        Assert.Equal(new CornerRadius(8), Assert.IsType<Border>(reset.Template.FindName("ActionChrome", reset)).CornerRadius);
        var saveChrome = Assert.IsType<Border>(save.Template.FindName("ActionChrome", save));
        Assert.Equal(Color.FromRgb(0x55, 0xCB, 0xFF), Assert.IsType<SolidColorBrush>(saveChrome.Background).Color);
    });

    private static string FlattenText(DependencyObject root) => string.Join(" ", Descendants(root).OfType<TextBlock>().Select(block => block.Text));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static bool IsVisualAncestor(DependencyObject ancestor, DependencyObject child)
    {
        for (var current = VisualTreeHelper.GetParent(child); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }

        return false;
    }

    private static SettingsViewModel CreateViewModel(FakeRuntime? runtime = null) => new(
        runtime ?? new FakeRuntime(), new FakeStartupService(), AppSettings.Default, new FakePicker(), new FakeDiscovery());

    private static RuntimeSnapshot Snapshot(double temperature) => new(
        RuntimeStatus.Active,
        new TemperatureReading(temperature, "test", "Test GPU", DateTimeOffset.UtcNow),
        null, null, null, null, DateTimeOffset.UtcNow, true);

    private static Button Button(SettingsView view, string name) => Assert.IsType<Button>(view.FindName(name));

    private static void AssertBinding<T>(SettingsView view, string name, DependencyProperty property, string path)
        where T : FrameworkElement
    {
        var element = Assert.IsType<T>(view.FindName(name));
        var expression = BindingOperations.GetBindingExpression(element, property);
        Assert.NotNull(expression);
        Assert.Equal(path, expression.ParentBinding.Path.Path);
    }

    private static T Arrange<T>(T element, double width, double height) where T : FrameworkElement
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        return element;
    }

    private sealed class FakePicker : IColorPickerService { public RgbColor? Pick(RgbColor current) => null; }
    private sealed class FakeDiscovery(params LightingDeviceInfo[] devices) : ILightingDeviceDiscovery
    {
        public Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LightingDeviceInfo>>(devices);
    }
    private sealed class FakeStartupService : IStartupService
    {
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class FakeRuntime : IThermalRuntime
    {
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
        public RuntimeSnapshot CurrentSnapshot { get; private set; } = new(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.MinValue);
        public AppSettings CurrentSettings { get; private set; } = AppSettings.Default;
        public void Publish(RuntimeSnapshot snapshot) { CurrentSnapshot = snapshot; SnapshotChanged?.Invoke(this, snapshot); }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdatePreferencesAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            settings.Validate();
            CurrentSettings = settings with { IsModeEnabled = CurrentSettings.IsModeEnabled };
            return Task.CompletedTask;
        }
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
