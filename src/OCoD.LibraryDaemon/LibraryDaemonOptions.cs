namespace OCoD.LibraryDaemon;

internal sealed record LibraryDaemonOptions(string LibraryPath, TimeSpan IdleExitDelay)
{
    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out LibraryDaemonOptions? options,
        out string? error)
    {
        options = null;
        error = null;
        string? libraryPath = null;
        var idleExitDelay = TimeSpan.FromMilliseconds(3000);

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (string.Equals(argument, "--library", StringComparison.Ordinal))
            {
                if (index + 1 >= arguments.Count)
                {
                    error = "--library requires a path.";
                    return false;
                }

                libraryPath = arguments[++index];
                continue;
            }

            if (argument.StartsWith("--library=", StringComparison.Ordinal))
            {
                libraryPath = argument["--library=".Length..];
                continue;
            }

            if (string.Equals(argument, "--idle-exit-ms", StringComparison.Ordinal))
            {
                if (index + 1 >= arguments.Count
                    || !int.TryParse(arguments[++index], out var milliseconds)
                    || milliseconds < 0)
                {
                    error = "--idle-exit-ms requires a non-negative integer.";
                    return false;
                }

                idleExitDelay = TimeSpan.FromMilliseconds(milliseconds);
            }
        }

        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            error = "A character library path is required (--library <path>).";
            return false;
        }

        options = new LibraryDaemonOptions(Path.GetFullPath(libraryPath), idleExitDelay);
        return true;
    }
}