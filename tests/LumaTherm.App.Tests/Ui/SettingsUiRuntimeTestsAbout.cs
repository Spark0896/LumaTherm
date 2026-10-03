using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LumaTherm.App.Localization;
using LumaTherm.App.Services;
using LumaTherm.App.ViewModels;
using LumaTherm.App.Views;
using LumaTherm.Core.Settings;
using LumaTherm.Core.Updates;

namespace LumaTherm.App.Tests.Ui;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class SettingsUiRuntimeTestsAbout(ThermalCoreStaFixture sta)
{
    [Fact]
    public void MainWindow_InfoNavigationShowsLocalizedAboutWithoutRecreatingTheWindow()
    {
        sta.Run(() =>
        {
            var localization = new LocalizationService(Application.Current.Resources);
            localization.Apply(AppLanguage.Russian);
            using var about = new AboutViewModel(new FixedFeed(), new NoOpLauncher(), localization);
            var shell = new MainWindow { AboutDataContext = about };
            try
            {
                Arrange(Assert.IsType<Border>(shell.Content), 1180, 720);
                var aboutButton = Assert.IsType<Button>(shell.FindName("AboutButton"));
                var aboutContent = Assert.IsType<AboutView>(shell.FindName("AboutContent"));
                var indicator = Assert.IsType<Border>(shell.FindName("AboutSelectionIndicator"));

                aboutButton.Command.Execute(null);
                shell.UpdateLayout();

                Assert.Equal(Visibility.Visible, aboutContent.Visibility);
                Assert.Equal(Visibility.Visible, indicator.Visibility);
                Assert.Equal(Visibility.Collapsed, Assert.IsAssignableFrom<FrameworkElement>(shell.FindName("DashboardContent")).Visibility);
                Assert.Contains("1.2.0", FlattenText(aboutContent), StringComparison.Ordinal);
                Assert.Contains("бесплат", FlattenText(aboutContent), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("открытым исходным кодом", FlattenText(aboutContent), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("MIT", FlattenText(aboutContent), StringComparison.Ordinal);
                Assert.Contains("управляет подсветкой", FlattenText(aboutContent), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("https://github.com/Spark0896/LumaTherm", FlattenText(aboutContent), StringComparison.Ordinal);

                var actionStyle = Assert.IsType<Style>(Application.Current.Resources["ActionButtonStyle"]);
                Assert.Same(actionStyle, Assert.IsType<Button>(aboutContent.FindName("RepositoryButton")).Style);
                Assert.Same(actionStyle, Assert.IsType<Button>(aboutContent.FindName("OpenReleaseButton")).Style);
                Assert.Same(Application.Current.Resources["PrimaryActionButtonStyle"],
                    Assert.IsType<Button>(aboutContent.FindName("CheckForUpdatesButton")).Style);

                var sameWindow = shell;
                localization.Apply(AppLanguage.English);
                shell.UpdateLayout();

                Assert.Same(sameWindow, shell);
                Assert.Contains("free", FlattenText(aboutContent), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("open source", FlattenText(aboutContent), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("controls thermal lighting", FlattenText(aboutContent), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Check for updates", FlattenText(aboutContent), StringComparison.Ordinal);
                var updateButton = Assert.IsType<Button>(aboutContent.FindName("CheckForUpdatesButton"));
                var actionLabel = Assert.Single(Descendants(updateButton).OfType<TextBlock>());
                Assert.Equal(Color.FromRgb(7, 26, 41), Assert.IsType<SolidColorBrush>(actionLabel.Foreground).Color);
            }
            finally
            {
                shell.Close();
                localization.Apply(AppLanguage.Russian);
            }
        });
    }

    [Fact]
    public void MainWindow_NavigationAccessibilityRefreshesSelectionAndNamesAcrossRuntimeLanguageSwitches()
    {
        sta.Run(() =>
        {
            var localization = new LocalizationService(Application.Current.Resources);
            localization.Apply(AppLanguage.Russian);
            var shell = new MainWindow();
            try
            {
                Arrange(Assert.IsType<Border>(shell.Content), 1180, 720);
                var home = Assert.IsType<Button>(shell.FindName("HomeButton"));
                var settings = Assert.IsType<Button>(shell.FindName("SettingsButton"));
                var about = Assert.IsType<Button>(shell.FindName("AboutButton"));

                Assert.Equal("Главная", AutomationProperties.GetName(home));
                Assert.Equal("Настройки", AutomationProperties.GetName(settings));
                Assert.Equal("О программе", AutomationProperties.GetName(about));
                Assert.Equal("Выбрано", AutomationProperties.GetItemStatus(home));
                Assert.Equal(string.Empty, AutomationProperties.GetItemStatus(settings));
                Assert.Equal(string.Empty, AutomationProperties.GetItemStatus(about));

                about.Command.Execute(null);
                Assert.Equal(string.Empty, AutomationProperties.GetItemStatus(home));
                Assert.Equal(string.Empty, AutomationProperties.GetItemStatus(settings));
                Assert.Equal("Выбрано", AutomationProperties.GetItemStatus(about));

                var sameWindow = shell;
                localization.Apply(AppLanguage.English);
                shell.UpdateLayout();

                Assert.Same(sameWindow, shell);
                Assert.Equal("Dashboard", AutomationProperties.GetName(home));
                Assert.Equal("Settings", AutomationProperties.GetName(settings));
                Assert.Equal("About", AutomationProperties.GetName(about));
                Assert.Equal(string.Empty, AutomationProperties.GetItemStatus(home));
                Assert.Equal(string.Empty, AutomationProperties.GetItemStatus(settings));
                Assert.Equal("Selected", AutomationProperties.GetItemStatus(about));

                settings.Command.Execute(null);
                Assert.Equal(string.Empty, AutomationProperties.GetItemStatus(home));
                Assert.Equal("Selected", AutomationProperties.GetItemStatus(settings));
                Assert.Equal(string.Empty, AutomationProperties.GetItemStatus(about));

                localization.Apply(AppLanguage.Russian);
                shell.UpdateLayout();
                Assert.Equal("Выбрано", AutomationProperties.GetItemStatus(settings));
            }
            finally
            {
                shell.Close();
                localization.Apply(AppLanguage.Russian);
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

    private static string FlattenText(DependencyObject root) =>
        string.Join(" ", Descendants(root).OfType<TextBlock>().Select(block => block.Text));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class FixedFeed : IReleaseFeed
    {
        public Task<ReleaseInfo> GetLatestStableAsync(CancellationToken cancellationToken) => Task.FromResult(new ReleaseInfo(
            new SemanticVersion(1, 2, 0),
            new Uri("https://github.com/Spark0896/LumaTherm/releases/tag/v1.2.0"),
            new Uri("https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm.exe")));
    }

    private sealed class NoOpLauncher : ILinkLauncher { public void Open(Uri uri) { } }
}
