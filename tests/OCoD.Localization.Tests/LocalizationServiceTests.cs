using System.Globalization;

namespace OCoD.Localization.Tests;

public sealed class LocalizationServiceTests
{
    private readonly LocalizationCatalog _catalog = LocalizationCatalog.LoadEmbedded();

    [Fact]
    public void Get_returns_copy_for_the_current_culture()
    {
        var service = new LocalizationService(_catalog, CultureInfo.GetCultureInfo("ja-JP"));

        Assert.Equal("言語", service.Get(LocKeys.MenuLanguage));
    }

    [Fact]
    public void Traditional_chinese_uses_simplified_chinese_copy()
    {
        var simplified = new LocalizationService(_catalog, CultureInfo.GetCultureInfo("zh-Hans"));
        var traditional = new LocalizationService(_catalog, CultureInfo.GetCultureInfo("zh-TW"));

        Assert.Equal(SupportedCultures.SimplifiedChinese, traditional.Culture.Name);
        Assert.Equal(
            simplified.Get(LocKeys.MenuLanguage),
            traditional.Get(LocKeys.MenuLanguage));
    }

    [Fact]
    public void Unknown_key_falls_back_to_the_key_itself()
    {
        var service = new LocalizationService(_catalog, CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Missing.Key", service.Get("Missing.Key"));
    }

    [Fact]
    public void SetCulture_raises_once_and_resolves_chinese_regions()
    {
        var service = new LocalizationService(_catalog, CultureInfo.GetCultureInfo("en"));
        var changes = new List<CultureInfo>();
        service.CultureChanged += changes.Add;

        service.SetCulture("zh-TW");
        service.SetCulture("zh-HK");

        Assert.Equal(SupportedCultures.SimplifiedChinese, service.Culture.Name);
        Assert.Single(changes);
        Assert.Equal(SupportedCultures.SimplifiedChinese, changes[0].Name);
    }

    [Fact]
    public void SetCulture_rejects_unknown_tags()
    {
        var service = new LocalizationService(_catalog, CultureInfo.GetCultureInfo("en"));

        Assert.False(service.SetCulture("not a culture"));
        Assert.Equal(SupportedCultures.English, service.Culture.Name);
    }

    [Fact]
    public void CreateDefault_uses_embedded_resources()
    {
        var service = LocalizationService.CreateDefault(CultureInfo.GetCultureInfo("ko-KR"));

        Assert.Equal(SupportedCultures.Korean, service.Culture.Name);
        Assert.Equal("언어", service.Get(LocKeys.MenuLanguage));
    }
}