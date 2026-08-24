using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using LumaTherm.Core.Settings;

namespace LumaTherm.App.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private const string EnglishResourcePath = "Resources/Strings.en-US.xaml";
    private const string RussianResourcePath = "Resources/Strings.ru-RU.xaml";
    private static readonly Uri EnglishResourceUri = CreateResourceUri(EnglishResourcePath);
    private static readonly Uri RussianResourceUri = CreateResourceUri(RussianResourcePath);
    private static readonly object ResourceLoadGate = new();
    private static readonly ConditionalWeakTable<ResourceDictionary, LanguageDictionaryMarker> LanguageDictionaries = new();
    private readonly ResourceDictionary _rootResources;
    private readonly CultureInfo _systemCulture;
    private ResourceDictionary _activeDictionary;
    private ResourceDictionary _englishDictionary;

    public LocalizationService(ResourceDictionary rootResources, CultureInfo? systemCulture = null)
    {
        _rootResources = rootResources ?? throw new ArgumentNullException(nameof(rootResources));
        _systemCulture = systemCulture ?? CultureInfo.CurrentUICulture;
        _englishDictionary = LoadDictionary(EnglishResourceUri);
        _activeDictionary = _englishDictionary;
        CurrentLanguage = AppLanguage.English;
    }

    public AppLanguage CurrentLanguage { get; private set; }

    public event EventHandler? LanguageChanged;

    public void Apply(AppLanguage language)
    {
        var resolvedLanguage = ResolveLanguage(language);
        var replacement = LoadDictionary(ResourceUriFor(resolvedLanguage));

        foreach (var dictionary in _rootResources.MergedDictionaries.Where(IsLanguageDictionary).ToArray())
        {
            _rootResources.MergedDictionaries.Remove(dictionary);
        }

        _rootResources.MergedDictionaries.Add(replacement);
        _activeDictionary = replacement;
        _englishDictionary = resolvedLanguage == AppLanguage.English
            ? replacement
            : LoadDictionary(EnglishResourceUri);

        if (CurrentLanguage == resolvedLanguage)
        {
            return;
        }

        CurrentLanguage = resolvedLanguage;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (TryGetString(_activeDictionary, key, out var localized)
            || TryGetString(_englishDictionary, key, out localized))
        {
            return localized;
        }

        throw new KeyNotFoundException($"Localization resource '{key}' was not found.");
    }

    public static bool IsLanguageDictionary(ResourceDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        if (LanguageDictionaries.TryGetValue(dictionary, out _)) return true;
        var source = dictionary.Source?.OriginalString;
        return source?.EndsWith(EnglishResourcePath, StringComparison.OrdinalIgnoreCase) == true
            || source?.EndsWith(RussianResourcePath, StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static ILocalizationService CreateFallback()
    {
        var service = new LocalizationService(new ResourceDictionary(), new CultureInfo("ru-RU"));
        service.Apply(AppLanguage.Russian);
        return service;
    }

    private AppLanguage ResolveLanguage(AppLanguage language) => language switch
    {
        AppLanguage.Russian => AppLanguage.Russian,
        AppLanguage.English => AppLanguage.English,
        AppLanguage.System when _systemCulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase) => AppLanguage.Russian,
        AppLanguage.System => AppLanguage.English,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported application language."),
    };

    private static bool TryGetString(ResourceDictionary dictionary, string key, out string value)
    {
        if (dictionary.Contains(key) && dictionary[key] is string resource)
        {
            value = resource;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static Uri ResourceUriFor(AppLanguage language) => language switch
    {
        AppLanguage.Russian => RussianResourceUri,
        AppLanguage.English => EnglishResourceUri,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Only resolved languages have resources."),
    };

    private static ResourceDictionary LoadDictionary(Uri source)
    {
        lock (ResourceLoadGate)
        {
            var dictionary = (ResourceDictionary)System.Windows.Application.LoadComponent(source);
            LanguageDictionaries.Add(dictionary, new LanguageDictionaryMarker());
            return dictionary;
        }
    }

    private sealed class LanguageDictionaryMarker;

    private static Uri CreateResourceUri(string resourcePath) => new($"/LumaTherm.App;component/{resourcePath}", UriKind.Relative);
}
