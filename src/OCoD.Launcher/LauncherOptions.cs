using OCoD.Localization;

namespace OCoD.Launcher;

internal enum LauncherCommand
{
    Start,
    Status,
    Stop,
    OpenManagement,
    EnsureRuntime,
    StopRuntime,
    RestartRuntime,
    RestartLibraryDaemon,
    SmokeTest,
    Import,
    Characters,
    StartCharacter,
    StopCharacter,
    ActivateCharacter,
    SetSettings,
    Help,
}

internal sealed record LauncherOptions(
    LauncherCommand Command,
    string? LibraryPath,
    string? DataDirectory,
    string? ComponentsDirectory,
    string? LanguageTag,
    string? CharacterId = null,
    string? ImportPath = null,
    double? Width = null,
    double? Height = null,
    bool? Flipped = null,
    bool? Topmost = null)
{
    public static bool TryParse(
        IReadOnlyList<string> arguments,
        LocalizationService localization,
        out LauncherOptions? options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(localization);

        options = null;
        error = null;

        var command = LauncherCommand.Start;
        string? library = null;
        string? dataDirectory = null;
        string? componentsDirectory = null;
        string? languageTag = Environment.GetEnvironmentVariable("OCOD_LANG");
        string? characterId = null;
        string? importPath = null;
        double? width = null;
        double? height = null;
        bool? flipped = null;
        bool? topmost = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            switch (argument)
            {
                case "--status":
                    command = LauncherCommand.Status;
                    continue;
                case "--stop":
                    command = LauncherCommand.Stop;
                    continue;
                case "--open":
                    command = LauncherCommand.OpenManagement;
                    continue;
                case "--ensure-runtime":
                    command = LauncherCommand.EnsureRuntime;
                    continue;
                case "--stop-runtime":
                    command = LauncherCommand.StopRuntime;
                    continue;
                case "--restart-runtime":
                    command = LauncherCommand.RestartRuntime;
                    continue;
                case "--restart-library-daemon":
                    command = LauncherCommand.RestartLibraryDaemon;
                    continue;
                case "--smoke-test":
                    command = LauncherCommand.SmokeTest;
                    continue;
                case "--characters":
                    command = LauncherCommand.Characters;
                    continue;
                case "--import":
                    command = LauncherCommand.Import;
                    if (index + 1 < arguments.Count)
                    {
                        importPath = arguments[++index];
                    }

                    continue;
                case "--start-character":
                    command = LauncherCommand.StartCharacter;
                    if (index + 1 < arguments.Count)
                    {
                        characterId = arguments[++index];
                    }

                    continue;
                case "--stop-character":
                    command = LauncherCommand.StopCharacter;
                    if (index + 1 < arguments.Count)
                    {
                        characterId = arguments[++index];
                    }

                    continue;
                case "--set-settings":
                    command = LauncherCommand.SetSettings;
                    if (index + 1 < arguments.Count)
                    {
                        characterId = arguments[++index];
                    }

                    continue;
                case "--activate-character":
                    command = LauncherCommand.ActivateCharacter;
                    if (index + 1 < arguments.Count)
                    {
                        characterId = arguments[++index];
                    }

                    continue;
                case "--help":
                case "-h":
                    command = LauncherCommand.Help;
                    continue;
            }

            if (TryReadValue(arguments, ref index, "--library", argument, out var libraryValue))
            {
                library = libraryValue;
            }
            else if (TryReadValue(arguments, ref index, "--data-dir", argument, out var dataValue))
            {
                dataDirectory = dataValue;
            }
            else if (TryReadValue(arguments, ref index, "--components-dir", argument, out var componentsValue))
            {
                componentsDirectory = componentsValue;
            }
            else if (TryReadValue(arguments, ref index, "--lang", argument, out var languageValue))
            {
                languageTag = languageValue;
            }
            else if (argument.StartsWith("--import=", StringComparison.Ordinal))
            {
                command = LauncherCommand.Import;
                importPath = argument["--import=".Length..];
            }
            else if (argument.StartsWith("--start-character=", StringComparison.Ordinal))
            {
                command = LauncherCommand.StartCharacter;
                characterId = argument["--start-character=".Length..];
            }
            else if (argument.StartsWith("--stop-character=", StringComparison.Ordinal))
            {
                command = LauncherCommand.StopCharacter;
                characterId = argument["--stop-character=".Length..];
            }
            else if (argument.StartsWith("--activate-character=", StringComparison.Ordinal))
            {
                command = LauncherCommand.ActivateCharacter;
                characterId = argument["--activate-character=".Length..];
            }
            else if (TryReadValue(arguments, ref index, "--width", argument, out var widthValue))
            {
                if (!double.TryParse(widthValue, out var parsedWidth))
                {
                    error = localization.Get(LocKeys.LauncherErrorWidthNumber);
                    return false;
                }

                width = parsedWidth;
            }
            else if (TryReadValue(arguments, ref index, "--height", argument, out var heightValue))
            {
                if (!double.TryParse(heightValue, out var parsedHeight))
                {
                    error = localization.Get(LocKeys.LauncherErrorHeightNumber);
                    return false;
                }

                height = parsedHeight;
            }
            else if (TryReadValue(arguments, ref index, "--flip", argument, out var flipValue))
            {
                if (!TryParseBoolean(flipValue, out var parsedFlip))
                {
                    error = localization.Get(LocKeys.LauncherErrorFlipOnOff);
                    return false;
                }

                flipped = parsedFlip;
            }
            else if (TryReadValue(arguments, ref index, "--topmost", argument, out var topmostValue))
            {
                if (!TryParseBoolean(topmostValue, out var parsedTopmost))
                {
                    error = localization.Get(LocKeys.LauncherErrorTopmostOnOff);
                    return false;
                }

                topmost = parsedTopmost;
            }
            else
            {
                error = localization.Get(LocKeys.LauncherErrorUnknownArgument, argument);
                return false;
            }
        }

        if (command == LauncherCommand.Import && string.IsNullOrWhiteSpace(importPath))
        {
            error = localization.Get(LocKeys.LauncherErrorImportPathRequired);
            return false;
        }

        if ((command is LauncherCommand.StartCharacter
                or LauncherCommand.StopCharacter
                or LauncherCommand.ActivateCharacter
                or LauncherCommand.SetSettings)
            && string.IsNullOrWhiteSpace(characterId))
        {
            error = localization.Get(LocKeys.LauncherErrorCharacterIdRequired);
            return false;
        }

        options = new LauncherOptions(
            command,
            Normalize(library),
            Normalize(dataDirectory),
            Normalize(componentsDirectory),
            Normalize(languageTag),
            Normalize(characterId),
            Normalize(importPath),
            width,
            height,
            flipped,
            topmost);
        return true;
    }

    /// <summary>Pre-scans --lang/OCOD_LANG so parse errors can be localized.</summary>
    public static string? FindLanguageTag(IReadOnlyList<string> arguments)
    {
        var tag = Environment.GetEnvironmentVariable("OCOD_LANG");

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (TryReadValue(arguments, ref index, "--lang", argument, out var value))
            {
                tag = value;
            }
        }

        return Normalize(tag);
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

    private static bool TryParseBoolean(string? value, out bool result)
    {
        result = false;

        if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}