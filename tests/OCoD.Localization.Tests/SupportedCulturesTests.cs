using System.Globalization;

namespace OCoD.Localization.Tests;

public sealed class SupportedCulturesTests
{
    [Theory]
    [InlineData("zh")]
    [InlineData("zh-CN")]
    [InlineData("zh-SG")]
    [InlineData("zh-TW")]
    [InlineData("zh-HK")]
    [InlineData("zh-MO")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("zh-Hant-TW")]
    public void Chinese_language_regions_resolve_to_simplified_chinese(string tag)
    {
        var resolved = SupportedCultures.Resolve(CultureInfo.GetCultureInfo(tag));

        Assert.Equal(SupportedCultures.SimplifiedChinese, resolved.Name);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("ja", "ja")]
    [InlineData("ja-JP", "ja")]
    [InlineData("ko", "ko")]
    [InlineData("ko-KR", "ko")]
    [InlineData("fr-FR", "en")]
    [InlineData("de-DE", "en")]
    public void Non_chinese_languages_map_to_a_supported_culture(string tag, string expected)
    {
        var resolved = SupportedCultures.Resolve(CultureInfo.GetCultureInfo(tag));

        Assert.Equal(expected, resolved.Name);
    }

    [Fact]
    public void Invariant_and_null_cultures_resolve_to_english()
    {
        Assert.Equal(SupportedCultures.English, SupportedCultures.Resolve(CultureInfo.InvariantCulture).Name);
        Assert.Equal(SupportedCultures.English, SupportedCultures.Resolve(null).Name);
    }

    [Theory]
    [InlineData("zh-TW")]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    [InlineData("ko-KR")]
    public void Valid_culture_tags_parse(string tag)
    {
        Assert.True(SupportedCultures.TryParse(tag, out var culture));
        Assert.Equal(tag, culture.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("not a culture")]
    public void Invalid_culture_tags_do_not_parse(string? tag)
    {
        Assert.False(SupportedCultures.TryParse(tag, out _));
    }
}