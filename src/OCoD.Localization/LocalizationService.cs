using System.Globalization;

namespace OCoD.Localization;

/// <summary>
/// Resolves user-visible text for the current UI culture.
/// Missing keys fall back to Simplified Chinese and finally to the key itself.
/// </summary>
public sealed class LocalizationService
{
    private readonly LocalizationCatalog _catalog;
    private CultureInfo _culture;

    public LocalizationService(LocalizationCatalog catalog, CultureInfo? culture = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _culture = SupportedCultures.Resolve(culture ?? CultureInfo.CurrentUICulture);
    }

    /// <summary>Creates a service with the localization resources embedded in this assembly.</summary>
    public static LocalizationService CreateDefault(CultureInfo? requestedCulture = null) =>
        new(LocalizationCatalog.LoadEmbedded(), requestedCulture);

    /// <summary>The resolved supported culture currently used for lookups.</summary>
    public CultureInfo Culture => _culture;

    /// <summary>Raised after the UI culture changes.</summary>
    public event Action<CultureInfo>? CultureChanged;

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Lookup(key) ?? key;
    }

    public string Get(string key, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var template = Get(key);
        return string.Format(_culture, template, arguments);
    }

    /// <summary>Changes the UI culture. Unsupported languages resolve to English, Chinese always to Simplified Chinese.</summary>
    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var resolved = SupportedCultures.Resolve(culture);
        if (string.Equals(resolved.Name, _culture.Name, StringComparison.Ordinal))
        {
            return;
        }

        _culture = resolved;
        CultureChanged?.Invoke(resolved);
    }

    /// <summary>Changes the UI culture from a BCP-47 tag. Returns false when the tag is not a culture.</summary>
    public bool SetCulture(string? tag)
    {
        if (!SupportedCultures.TryParse(tag, out var culture))
        {
            return false;
        }

        SetCulture(culture);
        return true;
    }

    private string? Lookup(string key)
    {
        if (_catalog.TryGetString(_culture, key, out var value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        var simplifiedChinese = SupportedCultures.Get(SupportedCultures.SimplifiedChinese);
        if (_catalog.TryGetString(simplifiedChinese, key, out var fallback) && !string.IsNullOrEmpty(fallback))
        {
            return fallback;
        }

        return null;
    }
}