using OCoD.Protocol;

namespace OCoD.GlobalDaemon;

internal sealed record GlobalDaemonOptions(
    string LibraryPath,
    string RuntimeRoot,
    string? LanguageTag,
    string? ComponentsDirectory)
{
    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out GlobalDaemonOptions? options,
        out string? error)
    {
        options = null;
        error = null;

        string? library = null;
        string? dataDirectory = null;
        string? languageTag = Environment.GetEnvironmentVariable("OCOD_LANG");
        string? componentsDirectory = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (TryReadValue(arguments, ref index, "--library", argument, out var libraryValue))
            {
                library = libraryValue;
            }
            else if (TryReadValue(arguments, ref index, "--data-dir", argument, out var dataValue))
            {
                dataDirectory = dataValue;
            }
            else if (TryReadValue(arguments, ref index, "--lang", argument, out var languageValue))
            {
                languageTag = languageValue;
            }
            else if (TryReadValue(arguments, ref index, "--components-dir", argument, out var componentsValue))
            {
                componentsDirectory = componentsValue;
            }
        }

        var runtimeRoot = OcodPaths.ResolveRuntimeRoot(dataDirectory);
        var libraryPath = OcodPaths.ResolveDefaultLibrary(AppContext.BaseDirectory, library);

        options = new GlobalDaemonOptions(
            libraryPath,
            runtimeRoot,
            string.IsNullOrWhiteSpace(languageTag) ? null : languageTag.Trim(),
            string.IsNullOrWhiteSpace(componentsDirectory) ? null : componentsDirectory.Trim());

        return true;
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
}