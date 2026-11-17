namespace OCoD.CharacterHost;

/// <summary>Command-line options accepted by the character runtime.</summary>
internal sealed record CharacterHostOptions(
    string? LanguageTag,
    bool SmokeTest,
    string? LibraryPath,
    string? ControlEndpoint,
    string? ControlToken,
    string? CharacterId)
{
    public static CharacterHostOptions Parse(
        IReadOnlyList<string> arguments,
        string? environmentLanguage = null)
    {
        string? languageTag = string.IsNullOrWhiteSpace(environmentLanguage)
            ? null
            : environmentLanguage.Trim();
        string? libraryPath = null;
        string? controlEndpoint = null;
        string? controlToken = null;
        string? characterId = null;
        var smokeTest = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (string.Equals(argument, "--smoke-test", StringComparison.Ordinal))
            {
                smokeTest = true;
                continue;
            }

            if (TryReadValue(arguments, ref index, "--lang", argument, out var language))
            {
                languageTag = language;
                continue;
            }

            if (TryReadValue(arguments, ref index, "--library", argument, out var library))
            {
                libraryPath = library;
                continue;
            }

            if (TryReadValue(arguments, ref index, "--control-endpoint", argument, out var endpoint))
            {
                controlEndpoint = endpoint;
                continue;
            }

            if (TryReadValue(arguments, ref index, "--control-token", argument, out var token))
            {
                controlToken = token;
                continue;
            }

            if (TryReadValue(arguments, ref index, "--character", argument, out var character))
            {
                characterId = character;
            }
        }

        return new CharacterHostOptions(
            Normalize(languageTag),
            smokeTest,
            Normalize(libraryPath),
            Normalize(controlEndpoint),
            Normalize(controlToken),
            Normalize(characterId));
    }

    private static bool TryReadValue(
        IReadOnlyList<string> arguments,
        ref int index,
        string name,
        string argument,
        out string? value)
    {
        value = null;

        if (string.Equals(argument, name, StringComparison.Ordinal))
        {
            if (index + 1 < arguments.Count)
            {
                value = arguments[++index];
            }

            return true;
        }

        var prefix = name + "=";
        if (argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = argument[prefix.Length..];
            return true;
        }

        return false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}