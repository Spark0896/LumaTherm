using LumaTherm.Core.Settings;

namespace LumaTherm.App.Localization;

public interface ILocalizationService
{
    AppLanguage CurrentLanguage { get; }
    event EventHandler? LanguageChanged;
    string Get(string key);
    void Apply(AppLanguage language);
}
