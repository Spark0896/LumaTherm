using System.Windows;
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
                    "Notification.Recovery", "Installer.Description",
                },
                key => Assert.True(english.Contains(key), $"Missing localization key: {key}"));
        });
    }

    private static ResourceDictionary Load(string source) => new()
    {
        Source = new Uri(source, UriKind.Relative),
    };
}
