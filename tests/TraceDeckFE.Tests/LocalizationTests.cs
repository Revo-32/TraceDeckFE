using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TraceDeckFE.Localization;
using TraceDeckFE.Models;
using TraceDeckFE.Services;

namespace TraceDeckFE.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData(AppLanguage.System, "ko-KR", "ko")]
    [InlineData(AppLanguage.System, "en-US", "en")]
    [InlineData(AppLanguage.System, "ja-JP", "en")]
    [InlineData(AppLanguage.English, "ko-KR", "en")]
    [InlineData(AppLanguage.Korean, "en-US", "ko")]
    [InlineData((AppLanguage)99, "fr-FR", "en")]
    public void ResolvesLanguageWithEnglishFallback(AppLanguage choice, string system, string expected) =>
        Assert.Equal(expected, L.ResolveCulture(choice, CultureInfo.GetCultureInfo(system)).Name);

    [Fact]
    public void CatalogsAreCompleteAndFormatArgumentsMatch()
    {
        var en = L.Catalog(CultureInfo.GetCultureInfo("en"));
        var ko = L.Catalog(CultureInfo.GetCultureInfo("ko"));
        Assert.True(en.Count >= 330);
        Assert.Equal(en.Keys.Order(), ko.Keys.Order());
        foreach (var (key, text) in en)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), key);
            Assert.False(string.IsNullOrWhiteSpace(ko[key]), key);
            Assert.Equal(Placeholders(text), Placeholders(ko[key]));
        }
    }
    private static string[] Placeholders(string text) => Regex.Matches(text, @"\{(\d+)(?:[^}]*)\}")
        .Select(m => m.Groups[1].Value).Order().ToArray();

    [Theory]
    [InlineData("Ui.Save", "Save", "저장")]
    [InlineData("Card.Project", "PROJECT", "프로젝트")]
    [InlineData("Settings.Language", "Language", "언어")]
    [InlineData("Status.Disconnected", "FH6  ○  Not Connected", "FH6  ○  연결 안 됨")]
    [InlineData("Dialog.UnsavedTitle", "Unsaved Project", "저장하지 않은 프로젝트")]
    public void TextResolvesFromEmbeddedResource(string key, string english, string korean)
    {
        Assert.Equal(english, L.Get(key, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(korean, L.Get(key, CultureInfo.GetCultureInfo("ko-KR")));
    }
    [Fact] public void UnsupportedCultureUsesEnglish() =>
        Assert.Equal("Save", L.Get("Ui.Save", CultureInfo.GetCultureInfo("fr-FR")));
    [Fact] public void UnknownKeyHasDiagnosticFallback() =>
        Assert.Equal("Future.Key", L.Get("Future.Key", CultureInfo.GetCultureInfo("ko")));
    [Fact] public void CatalogIsCachedAcrossRepeatedLookups()
    {
        var culture = CultureInfo.GetCultureInfo("ko");
        var original = L.Catalog(culture);
        for (var i=0; i<10000; i++) Assert.Same(original, L.Catalog(culture));
    }
    [Theory] [InlineData(AppLanguage.System)] [InlineData(AppLanguage.English)] [InlineData(AppLanguage.Korean)]
    public async Task LanguagePreferenceRoundTripsWithoutChangingProjectFields(AppLanguage language)
    {
        using var data = new TempData(); var service = new SettingsService(data);
        var settings = new ApplicationSettings { Language=language, WideWidth=471, FoldedCards=new(){ColorExpanded=false} };
        await service.SaveAsync(settings); var read=service.Load(out var warning);
        Assert.Null(warning); Assert.Equal(language,read.Language); Assert.Equal(471,read.WideWidth);
        Assert.False(read.FoldedCards.ColorExpanded);
    }
    [Theory] [InlineData("{}")] [InlineData("{\"language\":999}")] [InlineData("{\"language\":null}")] [InlineData("{\"language\":\"unsupported\"}")]
    public async Task MissingOrInvalidLanguageFallsBackSafely(string json)
    {
        using var data = new TempData(); var service = new SettingsService(data);
        await AtomicFile.WriteAsync(service.SettingsPath,Encoding.UTF8.GetBytes(json));
        Assert.Equal(AppLanguage.System,service.Load(out _).Language);
    }
    [Fact]
    public void AllShortcutActionsAndSettingsOptionsAreTranslated()
    {
        foreach (var culture in new[]{"en","ko"})
        {
            var catalog=L.Catalog(CultureInfo.GetCultureInfo(culture));
            foreach(var action in Enum.GetValues<ShortcutAction>()) Assert.True(catalog.ContainsKey("Action."+action),action.ToString());
            foreach(var option in Enum.GetNames<UiDensity>().Concat(Enum.GetNames<AnimationMode>())) Assert.True(catalog.ContainsKey("Option."+option),option);
            foreach(var key in catalog.Keys.Where(k=>k.StartsWith("Tip.") || k.StartsWith("Help."))) Assert.InRange(catalog[key].Length, 3, 220);
        }
    }
}
