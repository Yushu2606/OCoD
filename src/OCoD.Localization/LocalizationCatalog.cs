using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace OCoD.Localization;

/// <summary>
/// Loads the embedded translation files once and makes them available by culture.
/// Each supported culture must have exactly one embedded JSON file.
/// </summary>
public sealed class LocalizationCatalog
{
    private const string ResourcePrefix = "OCoD.Localization.Resources.strings.";

    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> _stringsByCulture;

    private LocalizationCatalog(
        FrozenDictionary<string, FrozenDictionary<string, string>> stringsByCulture)
    {
        _stringsByCulture = stringsByCulture;
    }

    /// <summary>Loads the translations shipped in the localization assembly.</summary>
    public static LocalizationCatalog LoadEmbedded(Assembly? assembly = null)
    {
        assembly ??= typeof(LocalizationCatalog).Assembly;

        var byCulture = new Dictionary<string, FrozenDictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var culture in SupportedCultures.All)
        {
            var resourceName = ResourcePrefix + culture.Name + ".json";
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Missing embedded localization resource '{resourceName}'.");

            var strings = ReadStrings(stream, resourceName);
            byCulture.Add(culture.Name, strings);
        }

        return new LocalizationCatalog(byCulture.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Returns the key/value pairs for the supported culture that <paramref name="culture"/> resolves to.</summary>
    public IReadOnlyDictionary<string, string> GetStrings(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var resolved = SupportedCultures.Resolve(culture);
        if (_stringsByCulture.TryGetValue(resolved.Name, out var strings))
        {
            return strings;
        }

        throw new InvalidOperationException(
            $"No localization resources were loaded for culture '{resolved.Name}'.");
    }

    internal bool TryGetString(CultureInfo culture, string key, out string? value)
    {
        var resolved = SupportedCultures.Resolve(culture);
        value = null;

        return _stringsByCulture.TryGetValue(resolved.Name, out var strings)
            && strings.TryGetValue(key, out value);
    }

    private static FrozenDictionary<string, string> ReadStrings(Stream stream, string resourceName)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"Localization resource '{resourceName}' must contain a JSON object.");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name))
                {
                    throw new InvalidDataException(
                        $"Localization resource '{resourceName}' contains an empty key.");
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(
                        $"Localization key '{property.Name}' in '{resourceName}' must have a string value.");
                }

                if (!values.TryAdd(property.Name, property.Value.GetString() ?? string.Empty))
                {
                    throw new InvalidDataException(
                        $"Localization resource '{resourceName}' contains duplicate key '{property.Name}'.");
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Localization resource '{resourceName}' is not valid JSON.", exception);
        }

        return values.ToFrozenDictionary(StringComparer.Ordinal);
    }
}