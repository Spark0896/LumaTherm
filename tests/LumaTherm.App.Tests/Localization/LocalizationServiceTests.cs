using System.Globalization;
using System.Windows;
using LumaTherm.App.Localization;
using LumaTherm.App.Tests.Ui;
using LumaTherm.Core.Settings;

namespace LumaTherm.App.Tests.Localization;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class LocalizationServiceTests(ThermalCoreStaFixture sta)
{
    [Fact]
    public void Apply_ExplicitLanguage_ChangesLookupAndRaisesLanguageChanged()
    {
        sta.Run(() =>
        {
            var service = new LocalizationService(new ResourceDictionary(), new CultureInfo("de-DE"));
            var changes = 0;
            service.LanguageChanged += (_, _) => changes++;

            service.Apply(AppLanguage.Russian);

            Assert.Equal(AppLanguage.Russian, service.CurrentLanguage);
            Assert.Equal("Настройки", service.Get("Nav.Settings"));
            Assert.Equal(1, changes);

            service.Apply(AppLanguage.English);

            Assert.Equal(AppLanguage.English, service.CurrentLanguage);
            Assert.Equal("Settings", service.Get("Nav.Settings"));
            Assert.Equal(2, changes);
        });
    }

    [Theory]
    [InlineData("ru-RU", AppLanguage.Russian, "Настройки")]
    [InlineData("de-DE", AppLanguage.English, "Settings")]
    public void Apply_SystemLanguage_UsesSupportedCultureOrEnglishFallback(string cultureName, AppLanguage expectedLanguage, string expectedSettings)
    {
        sta.Run(() =>
        {
            var service = new LocalizationService(new ResourceDictionary(), new CultureInfo(cultureName));

            service.Apply(AppLanguage.System);

            Assert.Equal(expectedLanguage, service.CurrentLanguage);
            Assert.Equal(expectedSettings, service.Get("Nav.Settings"));
        });
    }

    [Fact]
    public void Apply_ReplacesOnlyLanguageDictionaryWithoutDuplicates()
    {
        sta.Run(() =>
        {
            var root = new ResourceDictionary();
            var unrelated = new ResourceDictionary { ["Theme.Marker"] = "preserved" };
            root.MergedDictionaries.Add(unrelated);
            var service = new LocalizationService(root, new CultureInfo("en-US"));

            service.Apply(AppLanguage.Russian);
            service.Apply(AppLanguage.English);
            service.Apply(AppLanguage.Russian);

            Assert.Equal("preserved", root["Theme.Marker"]);
            Assert.Same(unrelated, root.MergedDictionaries.Single(dictionary => dictionary.Contains("Theme.Marker")));
            Assert.Single(root.MergedDictionaries, LocalizationService.IsLanguageDictionary);
            Assert.Equal(2, root.MergedDictionaries.Count);
        });
    }

    [Fact]
    public void Get_UnknownDevelopmentKey_Throws()
    {
        sta.Run(() =>
        {
            var service = new LocalizationService(new ResourceDictionary(), new CultureInfo("en-US"));

            service.Apply(AppLanguage.English);

            Assert.Throws<KeyNotFoundException>(() => service.Get("Missing.Key"));
        });
    }
}
