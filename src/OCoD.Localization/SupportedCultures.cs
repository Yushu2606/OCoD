using System.Globalization;

namespace OCoD.Localization;

/// <summary>
/// The cultures the first release ships with, and the fallback rules used to pick one.
/// All Chinese language regions, including Traditional Chinese, resolve to Simplified Chinese.
/// Every other unsupported language region resolves to English.
/// </summary>
public static class SupportedCultures
{
    public const string SimplifiedChinese = "zh-Hans";
    public const string English = "en";
    public const string Japanese = "ja";
    public const string Korean = "ko";

    public static IReadOnlyList<CultureInfo> All { get; } =
    [
        CultureInfo.GetCultureInfo(SimplifiedChinese),
        CultureInfo.GetCultureInfo(English),
        CultureInfo.GetCultureInfo(Japanese),
        CultureInfo.GetCultureInfo(Korean),
    ];

    /// <summary>Returns the supported culture that should be used for <paramref name="requested"/>.</summary>
    public static CultureInfo Resolve(CultureInfo? requested)
    {
        if (requested is null || string.IsNullOrEmpty(requested.Name))
        {
            return Get(English);
        }

        if (IsChinese(requested))
        {
            return Get(SimplifiedChinese);
        }

        return requested.TwoLetterISOLanguageName.ToLowerInvariant() switch
        {
            "en" => Get(English),
            "ja" => Get(Japanese),
            "ko" => Get(Korean),
            _ => Get(English),
        };
    }

    /// <summary>Returns a read-only culture for a supported culture name.</summary>
    public static CultureInfo Get(string name) => CultureInfo.GetCultureInfo(name);

    /// <summary>Parses a BCP-47 culture tag without deciding which supported culture it maps to.</summary>
    public static bool TryParse(string? tag, out CultureInfo culture)
    {
        culture = CultureInfo.InvariantCulture;

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        try
        {
            culture = CultureInfo.GetCultureInfo(tag.Trim());
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>True for any Chinese language region, including Traditional Chinese variants.</summary>
    public static bool IsChinese(CultureInfo culture) =>
        string.Equals(culture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);
}