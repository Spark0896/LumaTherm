using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Shapes;
using LumaTherm.App.Services;
using LumaTherm.App.ViewModels;
using LumaTherm.App.Views;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;

namespace LumaTherm.App.Tests.Ui;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class SettingsUiRuntimeTests
{
    private readonly ThermalCoreStaFixture _sta;

    public SettingsUiRuntimeTests(ThermalCoreStaFixture sta) => _sta = sta;

    [Fact]
    public void SettingsView_LoadsCompiledControlsAndResolvesRequiredBindings() => _sta.Run(() =>
    {
        var vm = CreateViewModel();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);

        Assert.Same(vm.PickColdColorCommand, Button(view, "ColdColorButton").Command);
        Assert.Same(vm.PickWarmColorCommand, Button(view, "WarmColorButton").Command);
        Assert.Same(vm.PickHotColorCommand, Button(view, "HotColorButton").Command);
        Assert.Same(vm.SaveCommand, Button(view, "SaveButton").Command);
        Assert.Same(vm.ResetDefaultsCommand, Button(view, "ResetButton").Command);

        AssertBinding<Slider>(view, "ColdTemperatureSlider", Slider.ValueProperty, nameof(SettingsViewModel.ColdTemperature));
        AssertBinding<Slider>(view, "WarmTemperatureSlider", Slider.ValueProperty, nameof(SettingsViewModel.WarmTemperature));
        AssertBinding<Slider>(view, "HotTemperatureSlider", Slider.ValueProperty, nameof(SettingsViewModel.HotTemperature));
        AssertBinding<Slider>(view, "SmoothingSlider", Slider.ValueProperty, nameof(SettingsViewModel.SmoothingSeconds));
        AssertBinding<TextBlock>(view, "ValidationText", TextBlock.TextProperty, nameof(SettingsViewModel.ValidationMessage));
        AssertBinding<ComboBox>(view, "LightingDeviceSelector", ComboBox.SelectedValueProperty, nameof(SettingsViewModel.SelectedLightingDeviceId));
    });

    [Fact]
    public void TemperatureEditors_UseExactLimitsLostFocusAndRussianCultureSafeNumbers() => _sta.Run(() =>
    {
        var vm = CreateViewModel();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);

        foreach (var name in new[] { "ColdTemperatureSlider", "WarmTemperatureSlider", "HotTemperatureSlider" })
        {
            var slider = Assert.IsType<Slider>(view.FindName(name));
            Assert.Equal(0, slider.Minimum);
            Assert.Equal(120, slider.Maximum);
        }

        var smoothing = Assert.IsType<Slider>(view.FindName("SmoothingSlider"));
        Assert.Equal(0.1, smoothing.Minimum);
        Assert.Equal(5, smoothing.Maximum);

        var editor = Assert.IsType<TextBox>(view.FindName("ColdTemperatureEditor"));
        var binding = BindingOperations.GetBindingExpression(editor, TextBox.TextProperty);
        Assert.NotNull(binding);
        Assert.Equal(UpdateSourceTrigger.LostFocus, binding.ParentBinding.UpdateSourceTrigger);
        Assert.Equal(XmlLanguage.GetLanguage("ru-RU"), editor.Language);
        editor.Text = "40,5";
        binding.UpdateSource();
        Assert.Equal(40.5, vm.ColdTemperature);
    });

    [Fact]
    public void MainWindow_NavigationCommandsSwitchCompiledContentAndSelectedAccessibilityState() => _sta.Run(() =>
    {
        using var dashboard = new MainViewModel(new FakeRuntime());
        var settings = CreateViewModel();
        var shell = new MainWindow { DataContext = dashboard, SettingsDataContext = settings };
        try
        {
            Arrange(Assert.IsType<Border>(shell.Content), 1180, 720);
            var home = Assert.IsType<Button>(shell.FindName("HomeButton"));
            var settingsButton = Assert.IsType<Button>(shell.FindName("SettingsButton"));
            var dashboardContent = Assert.IsType<DashboardView>(shell.FindName("DashboardContent"));
            var settingsContent = Assert.IsType<SettingsView>(shell.FindName("SettingsContent"));

            Assert.Same(shell.ShowDashboardCommand, home.Command);
            Assert.Same(shell.ShowSettingsCommand, settingsButton.Command);
            Assert.True(home.Focusable);
            Assert.True(settingsButton.Focusable);
            Assert.Equal("Выбрано", AutomationProperties.GetItemStatus(home));

            settingsButton.Command.Execute(null);

            Assert.Equal(Visibility.Collapsed, dashboardContent.Visibility);
            Assert.Equal(Visibility.Visible, settingsContent.Visibility);
            Assert.Same(settings, settingsContent.DataContext);
            Assert.Equal("", AutomationProperties.GetItemStatus(home));
            Assert.Equal("Выбрано", AutomationProperties.GetItemStatus(settingsButton));
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
        var view = Arrange(new SettingsView { DataContext = CreateViewModel() }, width, 552);

        Assert.Equal(compact, view.IsCompactLayout);
        Assert.Equal(compact ? 1 : 2, view.ActiveColumnCount);
        foreach (var name in new[] { "ColdTemperatureEditor", "WarmTemperatureEditor", "HotTemperatureEditor", "SmoothingEditor", "ResetButton", "SaveButton" })
        {
            var element = Assert.IsAssignableFrom<FrameworkElement>(view.FindName(name));
            Assert.True(element.ActualWidth > 0, $"{name} has no width.");
            Assert.True(element.ActualHeight > 0, $"{name} has no height.");
            var bounds = element.TransformToAncestor(view).TransformBounds(new Rect(element.RenderSize));
            Assert.True(bounds.Left >= -0.5, $"{name} starts outside the client: {bounds}.");
            Assert.True(bounds.Right <= width + 0.5, $"{name} clips horizontally: {bounds}.");
            Assert.True(bounds.Bottom <= view.SettingsScroller.ExtentHeight + 0.5, $"{name} ends beyond the scroll extent: {bounds}.");
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

            var selected = service.Pick(current);

            Assert.Equal(new RgbColor(1, 2, 3), selected);
            Assert.NotEqual(0, capturedHandle);
        }
        finally
        {
            owner.Close();
            Application.Current.MainWindow = previousMainWindow;
        }
    });

    [Fact]
    public void SettingsView_ValidationAndEveryEditorExposeRussianAccessibleNames() => _sta.Run(() =>
    {
        var vm = CreateViewModel();
        vm.ColdTemperature = 65;
        vm.SaveCommand.ExecuteAsync().GetAwaiter().GetResult();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);
        var validation = Assert.IsType<TextBlock>(view.FindName("ValidationText"));

        Assert.Equal("Температуры должны возрастать с шагом не менее 1 °C.", validation.Text);
        Assert.Equal(Visibility.Visible, validation.Visibility);
        Assert.Equal(Color.FromRgb(0xFF, 0x85, 0x8B), Assert.IsType<SolidColorBrush>(validation.Foreground).Color);
        Assert.Equal(validation.Text, AutomationProperties.GetName(validation));
        Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(validation));

        foreach (var name in new[]
        {
            "ColdColorButton", "WarmColorButton", "HotColorButton",
            "ColdTemperatureSlider", "WarmTemperatureSlider", "HotTemperatureSlider", "SmoothingSlider",
            "ColdTemperatureEditor", "WarmTemperatureEditor", "HotTemperatureEditor", "SmoothingEditor",
            "AutostartToggle", "MinimizeToTrayToggle", "NotificationsToggle", "LightingDeviceSelector",
            "ResetButton", "SaveButton",
        })
        {
            var element = Assert.IsAssignableFrom<FrameworkElement>(view.FindName(name));
            Assert.Matches("[А-Яа-яЁё]", AutomationProperties.GetName(element));
            Assert.True(element.Focusable, $"{name} is not keyboard reachable.");
        }
    });

    [Fact]
    public void SettingsView_DeviceSelectorRendersMultipleAndUnavailableStableIds() => _sta.Run(() =>
    {
        var saved = AppSettings.Default with { PreferredLightingDeviceId = "lamp-missing" };
        var vm = new SettingsViewModel(
            new FakeRuntime(),
            new FakeStartupService(),
            saved,
            new FakePicker(),
            new FakeDiscovery(new LightingDeviceInfo("lamp-a", "Desk Lamp", 8, true)));
        vm.DiscoverLightingDevicesCommand.ExecuteAsync().GetAwaiter().GetResult();
        var view = Arrange(new SettingsView { DataContext = vm }, 1104, 900);
        var selector = Assert.IsType<ComboBox>(view.FindName("LightingDeviceSelector"));

        Assert.Equal(Visibility.Visible, selector.Visibility);
        Assert.Equal(2, selector.Items.Count);
        Assert.Equal("lamp-missing", selector.SelectedValue);
        Assert.Contains("недоступно", Assert.IsType<LightingDeviceInfo>(selector.SelectedItem).Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("занято", vm.LightingHardwareStatus, StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public void SettingsView_UsesThermalTemplatesWithCompiledInteractionStates() => _sta.Run(() =>
    {
        var view = Arrange(new SettingsView { DataContext = CreateViewModel() }, 1104, 900);
        var slider = Assert.IsType<Slider>(view.FindName("ColdTemperatureSlider"));
        var toggle = Assert.IsType<CheckBox>(view.FindName("AutostartToggle"));
        var editor = Assert.IsType<TextBox>(view.FindName("ColdTemperatureEditor"));
        var reset = Assert.IsType<Button>(view.FindName("ResetButton"));
        var save = Assert.IsType<Button>(view.FindName("SaveButton"));
        slider.ApplyTemplate();
        toggle.ApplyTemplate();
        editor.ApplyTemplate();
        reset.ApplyTemplate();
        save.ApplyTemplate();

        var sliderTrack = Assert.IsType<Border>(slider.Template.FindName("ThermalSliderTrack", slider));
        Assert.Equal(new CornerRadius(4), sliderTrack.CornerRadius);
        Assert.Equal(Color.FromRgb(0x36, 0x3D, 0x43), Assert.IsType<SolidColorBrush>(sliderTrack.Background).Color);
        var interactiveTrack = Assert.IsType<Track>(slider.Template.FindName("PART_Track", slider));
        slider.Value = 35;
        slider.UpdateLayout();
        Assert.Equal(slider.Minimum, interactiveTrack.Minimum);
        Assert.Equal(slider.Maximum, interactiveTrack.Maximum);
        Assert.Equal(slider.Value, interactiveTrack.Value);
        AssertTemplateStates(slider.Template, "IsMouseOver", "IsKeyboardFocused", "IsEnabled");

        var switchTrack = Assert.IsType<Border>(toggle.Template.FindName("SwitchTrack", toggle));
        var switchThumb = Assert.IsType<Ellipse>(toggle.Template.FindName("SwitchThumb", toggle));
        Assert.Equal(new CornerRadius(9), switchTrack.CornerRadius);
        toggle.IsChecked = true;
        toggle.UpdateLayout();
        Assert.Equal(Color.FromRgb(0x50, 0xC8, 0xFF), Assert.IsType<SolidColorBrush>(switchTrack.Background).Color);
        Assert.Equal(HorizontalAlignment.Right, switchThumb.HorizontalAlignment);
        AssertTemplateStates(toggle.Template, "IsChecked", "IsMouseOver", "IsKeyboardFocused", "IsEnabled");

        var editorChrome = Assert.IsType<Border>(editor.Template.FindName("EditorChrome", editor));
        Assert.Equal(new CornerRadius(6), editorChrome.CornerRadius);
        Assert.Equal(Color.FromRgb(0x17, 0x1B, 0x20), Assert.IsType<SolidColorBrush>(editorChrome.Background).Color);
        AssertTemplateStates(editor.Template, "IsMouseOver", "IsKeyboardFocused", "IsEnabled");

        var resetChrome = Assert.IsType<Border>(reset.Template.FindName("ActionChrome", reset));
        var saveChrome = Assert.IsType<Border>(save.Template.FindName("ActionChrome", save));
        Assert.Equal(new CornerRadius(8), resetChrome.CornerRadius);
        Assert.Equal(Color.FromRgb(0x1B, 0x20, 0x25), Assert.IsType<SolidColorBrush>(resetChrome.Background).Color);
        Assert.Equal(Color.FromRgb(0x50, 0xC8, 0xFF), Assert.IsType<SolidColorBrush>(saveChrome.Background).Color);
        AssertTemplateStates(save.Template, "IsMouseOver", "IsPressed", "IsKeyboardFocused", "IsEnabled");
    });

    private static void AssertTemplateStates(ControlTemplate template, params string[] properties)
    {
        var triggerProperties = template.Triggers.OfType<Trigger>().Select(trigger => trigger.Property.Name).ToArray();
        foreach (var property in properties)
        {
            Assert.Contains(property, triggerProperties);
        }
    }

    private static SettingsViewModel CreateViewModel() => new(
        new FakeRuntime(),
        new FakeStartupService(),
        AppSettings.Default,
        new FakePicker(),
        new FakeDiscovery());

    private static Button Button(SettingsView view, string name) => Assert.IsType<Button>(view.FindName(name));

    private static void AssertBinding<T>(SettingsView view, string name, DependencyProperty property, string path)
        where T : FrameworkElement
    {
        var element = Assert.IsType<T>(view.FindName(name));
        var expression = BindingOperations.GetBindingExpression(element, property);
        Assert.NotNull(expression);
        Assert.Null(expression.Status == BindingStatus.Unattached ? "Binding is unattached." : null);
        Assert.Equal(path, expression.ParentBinding.Path.Path);
    }

    private static T Arrange<T>(T element, double width, double height) where T : FrameworkElement
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        return element;
    }

    private sealed class FakePicker : IColorPickerService
    {
        public RgbColor? Pick(RgbColor current) => null;
    }

    private sealed class FakeDiscovery(params LightingDeviceInfo[] devices) : ILightingDeviceDiscovery
    {
        public Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LightingDeviceInfo>>(devices);
    }

    private sealed class FakeStartupService : IStartupService
    {
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRuntime : IThermalRuntime
    {
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged { add { } remove { } }
        public RuntimeSnapshot CurrentSnapshot { get; } = new(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.MinValue);
        public AppSettings CurrentSettings { get; private set; } = AppSettings.Default;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            settings.Validate();
            CurrentSettings = settings;
            return Task.CompletedTask;
        }
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
