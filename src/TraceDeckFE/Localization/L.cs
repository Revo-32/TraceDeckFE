using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Markup;
using TraceDeckFE.Models;

namespace TraceDeckFE.Localization;

/// <summary>Immutable, embedded catalogs. UI language is resolved once at startup;
/// changing the preference never mutates an active editing session or numeric culture.</summary>
public static class L
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> English = new(() => Load("en"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Korean = new(() => Load("ko"));
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
    public static CultureInfo ResolveCulture(AppLanguage language, CultureInfo systemCulture) =>
        CultureInfo.GetCultureInfo(language switch
        {
            AppLanguage.Korean => "ko",
            AppLanguage.English => "en",
            _ => systemCulture.TwoLetterISOLanguageName == "ko" ? "ko" : "en"
        });

    public static void Initialize(AppLanguage language, CultureInfo? systemCulture = null) =>
        Culture = ResolveCulture(language, systemCulture ?? CultureInfo.CurrentUICulture);

    public static IReadOnlyDictionary<string, string> Catalog(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "ko" ? Korean.Value : English.Value;

    public static string Get(string key) => Get(key, Culture);
    public static string Get(string key, CultureInfo culture) =>
        Catalog(culture).TryGetValue(key, out var value) ? value : English.Value.GetValueOrDefault(key, key);
    public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream($"TraceDeckFE.Resources.Strings.{language}.json")
            ?? throw new InvalidOperationException($"Missing UI catalog: {language}");
        return new ReadOnlyDictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!);
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => L.Get(Key);
}
