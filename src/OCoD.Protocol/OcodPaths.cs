namespace OCoD.Protocol;

/// <summary>Resolves the per-user runtime data and the platform-specific character library defaults.</summary>
public static class OcodPaths
{
    public const string DataDirectoryEnvironmentVariable = "OCOD_DATA_DIR";
    public const string LibraryEnvironmentVariable = "OCOD_LIBRARY";
    public const string ComponentsDirectoryEnvironmentVariable = "OCOD_COMPONENTS_DIR";

    /// <summary>
    /// Runtime data used by the global daemon and logs.
    /// macOS follows the platform convention (Application Support); Windows uses LocalAppData.
    /// </summary>
    public static string ResolveRuntimeRoot(string? dataDirectory = null)
    {
        var configured = FirstNonEmpty(
            dataDirectory,
            Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable));

        if (configured is not null)
        {
            return Path.GetFullPath(configured);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", "OCoD");
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OCoD");
        }

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(
            string.IsNullOrWhiteSpace(xdgDataHome)
                ? Path.Combine(home, ".local", "share")
                : xdgDataHome,
            "ocod");
    }

    /// <summary>
    /// macOS shares one library below Application Support. Windows keeps the library next to the
    /// distribution directory so moving that directory moves the characters with it.
    /// </summary>
    public static string ResolveDefaultLibrary(string launcherDirectory, string? library = null)
    {
        var configured = FirstNonEmpty(
            library,
            Environment.GetEnvironmentVariable(LibraryEnvironmentVariable));

        if (configured is not null)
        {
            return Path.GetFullPath(configured);
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.GetFullPath(Path.Combine(launcherDirectory, "OCoDData", "Library"));
        }

        return Path.Combine(ResolveRuntimeRoot(), "Library");
    }

    public static string LogsDirectory(string runtimeRoot) => Path.Combine(runtimeRoot, "logs");

    public static string GlobalDaemonLockFile(string runtimeRoot) =>
        Path.Combine(runtimeRoot, "global-daemon.lock");

    public static string GlobalDaemonLogFile(string runtimeRoot) =>
        Path.Combine(LogsDirectory(runtimeRoot), "global-daemon.log");

    public static string LauncherLogFile(string runtimeRoot) =>
        Path.Combine(LogsDirectory(runtimeRoot), "launcher.log");

    public static string CharacterRuntimeLogFile(string runtimeRoot) =>
        Path.Combine(LogsDirectory(runtimeRoot), "character-runtime.log");

    public static string LibraryMetadataDirectory(string libraryPath) =>
        Path.Combine(libraryPath, ".ocod");

    public static string LibraryLockFile(string libraryPath) =>
        Path.Combine(LibraryMetadataDirectory(libraryPath), "library.lock");

    public static string LibraryLogFile(string libraryPath) =>
        Path.Combine(LibraryMetadataDirectory(libraryPath), "logs", "library-daemon.log");

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first)
            ? first
            : !string.IsNullOrWhiteSpace(second)
                ? second
                : null;
}