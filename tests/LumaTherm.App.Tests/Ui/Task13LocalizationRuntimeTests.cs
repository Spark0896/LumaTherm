using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LumaTherm.App.Controls;
using LumaTherm.App.Localization;
using LumaTherm.App.Services;
using LumaTherm.App.ViewModels;
using LumaTherm.App.Views;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;
using LumaTherm.Core.Updates;

namespace LumaTherm.App.Tests.Ui;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class Task13LocalizationRuntimeTests(ThermalCoreStaFixture sta)
{
    [Fact]
    public void ExistingShellDashboardSettingsAndAbout_SwitchRussianEnglishRussianInPlace()
    {
        sta.Run(() =>
        {
            var localization = new LocalizationService(Application.Current.Resources, new CultureInfo("en-US"));
            localization.Apply(AppLanguage.Russian);
            var runtime = new FakeRuntime();
            using var settings = new SettingsViewModel(
                runtime, new FakeStartup(), AppSettings.Default, new FakePicker(), new FakeDiscovery(), localization);
            using var dashboard = new MainViewModel(runtime, localization: localization);
            using var about = new AboutViewModel(new FakeFeed(), new FakeLauncher(), localization);
            dashboard.SynchronizeProfile(settings);
            var shell = new MainWindow
            {
                DataContext = dashboard,
                SettingsDataContext = settings,
                AboutDataContext = about,
            };
            try
            {
                Arrange(Assert.IsType<Border>(shell.Content), 960, 620);
                var sameShell = shell;
                var home = Assert.IsType<Button>(shell.FindName("HomeButton"));
                var settingsButton = Assert.IsType<Button>(shell.FindName("SettingsButton"));
                var aboutButton = Assert.IsType<Button>(shell.FindName("AboutButton"));
                var dashboardView = Assert.IsType<DashboardView>(shell.FindName("DashboardContent"));
                var settingsView = Assert.IsType<SettingsView>(shell.FindName("SettingsContent"));

                Assert.Contains("Термосинхронизация", FlattenText(dashboardView), StringComparison.Ordinal);
                Assert.Equal("Главная", AutomationProperties.GetName(home));
                Assert.Equal("Выбрано", AutomationProperties.GetItemStatus(home));
                Assert.Equal("Свернуть окно", AutomationProperties.GetName(Assert.IsType<Button>(shell.FindName("MinimizeButton"))));

                settingsButton.Command.Execute(null);
                settings.SmoothingSeconds = 0;
                settings.SaveCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.Equal("Сглаживание должно быть от 0,1 до 5,0 секунд.", settings.ValidationMessage);

                localization.Apply(AppLanguage.English);
                PumpBindings(shell);

                Assert.Same(sameShell, shell);
                Assert.Contains("Thermal synchronization", FlattenText(dashboardView), StringComparison.Ordinal);
                Assert.Contains("Settings", FlattenText(settingsView), StringComparison.Ordinal);
                Assert.Equal("Home", AutomationProperties.GetName(home));
                Assert.Equal("Settings", AutomationProperties.GetName(settingsButton));
                Assert.Equal("About", AutomationProperties.GetName(aboutButton));
                Assert.Equal("Selected", AutomationProperties.GetItemStatus(settingsButton));
                Assert.Equal("Minimize window", AutomationProperties.GetName(Assert.IsType<Button>(shell.FindName("MinimizeButton"))));
                Assert.Equal("Smoothing must be between 0.1 and 5.0 seconds.", settings.ValidationMessage);

                aboutButton.Command.Execute(null);
                PumpBindings(shell);
                Assert.Contains("About LumaTherm", FlattenText(Assert.IsType<AboutView>(shell.FindName("AboutContent"))), StringComparison.Ordinal);

                localization.Apply(AppLanguage.Russian);
                PumpBindings(shell);

                Assert.Equal("О программе", AutomationProperties.GetName(aboutButton));
                Assert.Equal("Выбрано", AutomationProperties.GetItemStatus(aboutButton));
                Assert.Equal("Сглаживание должно быть от 0,1 до 5,0 секунд.", settings.ValidationMessage);
            }
            finally
            {
                shell.Close();
                localization.Apply(AppLanguage.English);
            }
        });
    }

    [Fact]
    public void SavedUnavailableDevice_LanguageRoundTripPreservesSelectionAndUnavailableProjection()
    {
        sta.Run(() =>
        {
            var localization = new LocalizationService(Application.Current.Resources, new CultureInfo("ru-RU"));
            localization.Apply(AppLanguage.Russian);
            var saved = AppSettings.Default with { PreferredLightingDeviceId = "lamp-missing" };
            using var settings = new SettingsViewModel(
                new FakeRuntime(),
                new FakeStartup(),
                saved,
                new FakePicker(),
                new FakeDiscovery(new LumaTherm.Core.Lighting.LightingDeviceInfo("lamp-a", "Desk Lamp", 8, true)),
                localization);

            try
            {
                settings.DiscoverLightingDevicesCommand.ExecuteAsync().GetAwaiter().GetResult();

                AssertSavedUnavailable(settings,
                    "Подсветка недоступна",
                    "Сохранённое устройство недоступно или занято другим контроллером.",
                    "lamp-missing (недоступно)");

                localization.Apply(AppLanguage.English);
                AssertSavedUnavailable(settings,
                    "Lighting is unavailable",
                    "The saved device is unavailable or in use by another controller.",
                    "lamp-missing (unavailable)");

                localization.Apply(AppLanguage.Russian);
                AssertSavedUnavailable(settings,
                    "Подсветка недоступна",
                    "Сохранённое устройство недоступно или занято другим контроллером.",
                    "lamp-missing (недоступно)");
            }
            finally
            {
                localization.Apply(AppLanguage.English);
            }
        });
    }

    [Fact]
    public void ExistingAboutSecondaryText_ResolvesMeaningfulContrastInRussianAndEnglish()
    {
        sta.Run(() =>
        {
            var localization = new LocalizationService(Application.Current.Resources, new CultureInfo("ru-RU"));
            localization.Apply(AppLanguage.Russian);
            using var about = new AboutViewModel(new FakeFeed(), new FakeLauncher(), localization);
            var view = new AboutView { DataContext = about };
            var host = new Window
            {
                Content = view,
                Width = 884,
                Height = 552,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            try
            {
                host.Show();
                PumpBindings(view);
                AssertAboutSecondaryContrast(view,
                    "Версия, лицензия, исходный код и обновления",
                    "LumaTherm управляет подсветкой по температуре GPU на совместимых устройствах.",
                    "LumaTherm — бесплатная программа с открытым исходным кодом по лицензии MIT.");

                localization.Apply(AppLanguage.English);
                PumpBindings(view);

                AssertAboutSecondaryContrast(view,
                    "Version, license, source code, and updates",
                    "LumaTherm controls thermal lighting by linking GPU temperature to compatible lighting devices.",
                    "LumaTherm is free and open source software under the MIT License.");
            }
            finally
            {
                host.Close();
                localization.Apply(AppLanguage.English);
            }
        });
    }

    private static void AssertSavedUnavailable(
        SettingsViewModel settings,
        string expectedName,
        string expectedStatus,
        string expectedChoiceName)
    {
        Assert.Equal("lamp-missing", settings.SelectedLightingDeviceId);
        Assert.Equal(expectedName, settings.LightingDeviceName);
        Assert.Equal(expectedStatus, settings.LightingHardwareStatus);
        var choice = Assert.Single(settings.LightingDevices, device => device.Id == "lamp-missing");
        Assert.False(choice.IsAvailable);
        Assert.Equal(expectedChoiceName, choice.Name);
    }

    [Fact]
    public void EnglishCompiledSurfaces_ExposeNoCyrillicTextOrAccessibilityLabelsAndKeepIdleDeviceState()
    {
        sta.Run(() =>
        {
            var localization = new LocalizationService(Application.Current.Resources, new CultureInfo("en-US"));
            localization.Apply(AppLanguage.English);
            var runtime = new FakeRuntime();
            using var settings = new SettingsViewModel(
                runtime, new FakeStartup(), AppSettings.Default, new FakePicker(), new FakeDiscovery(), localization);
            using var dashboard = new MainViewModel(runtime, localization: localization);
            using var about = new AboutViewModel(new FakeFeed(), new FakeLauncher(), localization);
            var shell = new MainWindow { DataContext = dashboard, SettingsDataContext = settings, AboutDataContext = about };
            var lighting = new LightingTestViewModel(runtime, ThermalProfile.Default, (_, _) => Task.CompletedTask, localization);
            var lightingWindow = new LightingTestWindow(lighting);
            try
            {
                Arrange(Assert.IsType<Border>(shell.Content), 960, 620);
                Arrange(Assert.IsType<Border>(lightingWindow.Content), 620, 560);
                var visibleText = FlattenText(shell) + " " + FlattenText(lightingWindow);
                var accessibleText = string.Join(" ", Descendants(shell).Concat(Descendants(lightingWindow))
                    .OfType<DependencyObject>().Select(AutomationProperties.GetName));

                Assert.DoesNotMatch("[А-Яа-яЁё]", visibleText);
                Assert.DoesNotMatch("[А-Яа-яЁё]", accessibleText);
                var dot = Assert.IsType<System.Windows.Shapes.Ellipse>(shell.FindName("DeviceStatusDot"));
                Assert.Equal(Color.FromRgb(0xB0, 0xBB, 0xC4), Assert.IsType<SolidColorBrush>(dot.Fill).Color);
            }
            finally
            {
                lightingWindow.CloseAfterCleanup();
                shell.Close();
                lighting.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void LocalizedHeadingsAndStatusText_HaveMeaningfulContrastAndFitMinimumClient()
    {
        sta.Run(() =>
        {
            var localization = new LocalizationService(Application.Current.Resources, new CultureInfo("ru-RU"));
            localization.Apply(AppLanguage.Russian);
            var runtime = new FakeRuntime();
            using var settings = new SettingsViewModel(
                runtime, new FakeStartup(), AppSettings.Default, new FakePicker(), new FakeDiscovery(), localization);
            using var dashboard = new MainViewModel(runtime, localization: localization);
            var dashboardView = Arrange(new DashboardView { DataContext = dashboard }, 884, 552);
            var settingsView = Arrange(new SettingsView { DataContext = settings }, 884, 552);
            try
            {
                var heading = Assert.IsType<TextBlock>(settingsView.FindName("ColorScaleHeading"));
                var panel = Assert.IsType<SolidColorBrush>(Application.Current.Resources["PanelBackgroundBrush"]);
                var status = Assert.IsType<TextBlock>(dashboardView.FindName("ModeStatusText"));
                Assert.Same(Application.Current.Resources["PrimaryTextBrush"], heading.Foreground);
                Assert.True(Contrast(Assert.IsType<SolidColorBrush>(heading.Foreground).Color, panel.Color) >= 4.5);
                Assert.True(Contrast(Assert.IsType<SolidColorBrush>(status.Foreground).Color, panel.Color) >= 4.5);

                foreach (var block in Descendants(dashboardView).Concat(Descendants(settingsView)).OfType<TextBlock>()
                    .Where(block => block.IsVisible && !string.IsNullOrWhiteSpace(block.Text) && block.ActualWidth > 0))
                {
                    Assert.True(
                        block.DesiredSize.Width <= block.ActualWidth + 0.5
                        || block.TextWrapping != TextWrapping.NoWrap
                        || block.TextTrimming != TextTrimming.None,
                        $"Localized text can clip at minimum size: '{block.Text}' ({block.DesiredSize.Width:0.#}>{block.ActualWidth:0.#}).");
                }
            }
            finally
            {
                localization.Apply(AppLanguage.English);
            }
        });
    }

    [Fact]
    public void ChromeButtons_UseVectorGeometryWithoutFontFallbackDependency()
    {
        sta.Run(() =>
        {
            var shell = new MainWindow();
            try
            {
                Arrange(Assert.IsType<Border>(shell.Content), 1180, 720);
                var buttons = new[]
                {
                    shell.MinimizeButton,
                    shell.MaximizeButton,
                    shell.CloseButton,
                };
                foreach (var button in buttons)
                {
                    var path = Assert.IsType<Path>(button.Content);
                    Assert.NotNull(path.Data);
                    Assert.Equal(Brushes.Transparent, path.Fill);
                    Assert.Same(button.Foreground, path.Stroke);
                }
            }
            finally
            {
                shell.Close();
            }
        });
    }

    private static T Arrange<T>(T element, double width, double height) where T : FrameworkElement
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        return element;
    }

    private static void PumpBindings(FrameworkElement element)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        element.UpdateLayout();
    }

    private static string FlattenText(DependencyObject root) =>
        string.Join(" ", Descendants(root).OfType<TextBlock>().Select(block => block.Text));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }

    private static double Contrast(Color foreground, Color background)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                var normalized = value / 255d;
                return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }
        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static void AssertAboutSecondaryContrast(AboutView view, params string[] expectedTexts)
    {
        var panel = Assert.IsType<SolidColorBrush>(Application.Current.Resources["PanelBackgroundBrush"]);
        foreach (var expected in expectedTexts)
        {
            var blocks = Descendants(view).OfType<TextBlock>().ToArray();
            var matches = blocks.Where(block => block.Text == expected).ToArray();
            Assert.True(matches.Length == 1,
                $"Expected one About text '{expected}', found {matches.Length}. Actual: {string.Join(" | ", blocks.Select(block => block.Text))}");
            var block = matches[0];
            var foreground = Assert.IsType<SolidColorBrush>(block.Foreground);
            Assert.True(Contrast(foreground.Color, panel.Color) >= 4.5,
                $"About secondary text '{expected}' has insufficient contrast ({foreground.Color} on {panel.Color}).");
        }
    }

    private sealed class FakeRuntime : IThermalRuntime
    {
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged { add { } remove { } }
        public RuntimeSnapshot CurrentSnapshot { get; private set; } = new(
            RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.MinValue, false);
        public AppSettings CurrentSettings { get; private set; } = AppSettings.Default;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ILightingTestSession> BeginLightingTestAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ILightingTestSession>(new FakeLightingSession());
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdatePreferencesAsync(AppSettings settings, CancellationToken cancellationToken)
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

    private sealed class FakeLightingSession : ILightingTestSession
    {
        public Task SetTemperatureAsync(double celsius, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetProfileAsync(ThermalProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeStartup : IStartupService
    {
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePicker : IColorPickerService
    {
        public RgbColor? Pick(RgbColor current) => null;
    }

    private sealed class FakeDiscovery(params LumaTherm.Core.Lighting.LightingDeviceInfo[] devices) : ILightingDeviceDiscovery
    {
        public Task<IReadOnlyList<LumaTherm.Core.Lighting.LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LumaTherm.Core.Lighting.LightingDeviceInfo>>(devices);
    }

    private sealed class FakeFeed : IReleaseFeed
    {
        public Task<ReleaseInfo> GetLatestStableAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeLauncher : ILinkLauncher
    {
        public void Open(Uri uri) { }
    }
}
