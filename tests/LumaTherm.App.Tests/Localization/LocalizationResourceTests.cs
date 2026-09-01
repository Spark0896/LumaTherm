using System.Windows;
using System.Windows.Media;
using LumaTherm.App.Tests.Ui;

namespace LumaTherm.App.Tests.Localization;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class LocalizationResourceTests(ThermalCoreStaFixture sta)
{
    [Fact]
    public void RussianAndEnglishResources_HaveTheSameCompleteKeySet()
    {
        sta.Run(() =>
        {
            var english = Load("/LumaTherm.App;component/Resources/Strings.en-US.xaml");
            var russian = Load("/LumaTherm.App;component/Resources/Strings.ru-RU.xaml");

            Assert.NotEmpty(english.Keys);
            Assert.Equal(english.Keys.Cast<object>().Order(), russian.Keys.Cast<object>().Order());
        });
    }

    [Fact]
    public void EnglishResources_ContainFoundationKeysForEveryPlannedSurface()
    {
        sta.Run(() =>
        {
            var english = Load("/LumaTherm.App;component/Resources/Strings.en-US.xaml");

            Assert.All(
                new[]
                {
                    "Dashboard.Title", "Settings.Title", "Tray.Open", "About.Title", "Update.Checking",
                    "Validation.TemperatureRange", "TestWindow.Title", "Nav.Settings", "Accessibility.CloseWindow",
                    "Notification.Recovery", "Installer.Description", "TestWindow.SelectedPoint",
                    "TestWindow.PointTemperature", "TestWindow.PointColor",
                },
                key => Assert.True(english.Contains(key), $"Missing localization key: {key}"));
        });
    }

    [Fact]
    public void DarkThemeSecondaryMutedAndControlColorsMeetReadableContrastTargets()
    {
        sta.Run(() =>
        {
            var resources = Application.Current.Resources;
            var panel = Assert.IsType<Color>(resources["PanelBackground"]);
            var secondary = Assert.IsType<Color>(resources["SecondaryText"]);
            var muted = Assert.IsType<Color>(resources["MutedText"]);
            var icon = Assert.IsType<SolidColorBrush>(resources["IconIdleBrush"]).Color;

            Assert.True(Contrast(secondary, panel) >= 9, $"Secondary text contrast was {Contrast(secondary, panel):F2}:1.");
            Assert.True(Contrast(muted, panel) >= 7, $"Muted text contrast was {Contrast(muted, panel):F2}:1.");
            Assert.True(Contrast(icon, panel) >= 6, $"Control icon contrast was {Contrast(icon, panel):F2}:1.");
        });
    }

    private static ResourceDictionary Load(string source) => new()
    {
        Source = new Uri(source, UriKind.Relative),
    };

    private static double Contrast(Color first, Color second)
    {
        var high = Math.Max(Luminance(first), Luminance(second));
        var low = Math.Min(Luminance(first), Luminance(second));
        return (high + 0.05) / (low + 0.05);
    }

    private static double Luminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
