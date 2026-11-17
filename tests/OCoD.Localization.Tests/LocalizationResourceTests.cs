using System.Globalization;
using System.Reflection;

namespace OCoD.Localization.Tests;

public sealed class LocalizationResourceTests
{
    private readonly LocalizationCatalog _catalog = LocalizationCatalog.LoadEmbedded();

    [Fact]
    public void Every_supported_culture_has_the_same_non_empty_keys()
    {
        var simplifiedChinese = _catalog.GetStrings(
            CultureInfo.GetCultureInfo(SupportedCultures.SimplifiedChinese));

        Assert.NotEmpty(simplifiedChinese);

        foreach (var culture in SupportedCultures.All)
        {
            var strings = _catalog.GetStrings(culture);
            Assert.Equal(
                simplifiedChinese.Keys.Order(StringComparer.Ordinal),
                strings.Keys.Order(StringComparer.Ordinal));

            foreach (var (key, value) in strings)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{culture.Name} has an empty value for '{key}'.");
            }
        }
    }

    [Fact]
    public void Traditional_chinese_reads_the_simplified_chinese_resource_set()
    {
        var simplified = _catalog.GetStrings(CultureInfo.GetCultureInfo("zh-Hans"));
        var traditional = _catalog.GetStrings(CultureInfo.GetCultureInfo("zh-Hant-HK"));
        var english = _catalog.GetStrings(CultureInfo.GetCultureInfo("en"));

        Assert.Same(simplified, traditional);
        Assert.NotSame(english, traditional);
    }

    [Fact]
    public void Every_declared_key_has_copy_in_every_culture()
    {
        var declaredKeys = typeof(LocKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(declaredKeys);

        foreach (var culture in SupportedCultures.All)
        {
            var strings = _catalog.GetStrings(culture);
            foreach (var key in declaredKeys)
            {
                Assert.True(strings.ContainsKey(key), $"{culture.Name} is missing '{key}'.");
            }
        }
    }
}