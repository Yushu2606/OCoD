using System.Security.Cryptography;
using System.Text;

namespace OCoD.Protocol;

/// <summary>Derives short, per-user Unix-domain socket paths for daemon discovery.</summary>
public static class EndpointPaths
{
    private static readonly string SocketDirectory =
        Path.Combine(Path.GetTempPath(), "ocod");

    public static string ForLibrary(string libraryPath) =>
        Path.Combine(EnsureSocketDirectory(), $"lib-{Hash(NormalizePath(libraryPath))}.sock");

    public static string ForGlobalDaemon(string runtimeRoot) =>
        Path.Combine(EnsureSocketDirectory(), $"global-{Hash(NormalizePath(runtimeRoot))}.sock");

    public static string NewCharacterControlEndpoint()
    {
        var suffix = Guid.NewGuid().ToString("N")[..16];
        return Path.Combine(EnsureSocketDirectory(), $"char-{suffix}.sock");
    }

    public static string EnsureSocketDirectory()
    {
        Directory.CreateDirectory(SocketDirectory);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(
                    SocketDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (PlatformNotSupportedException)
            {
                // Older runtimes/filesystems may not support mode changes; the temp directory is per-user.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return SocketDirectory;
    }

    public static string NormalizePath(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? normalized.ToLowerInvariant() : normalized;
    }

    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    public static void ValidateEndpointLength(string endpoint)
    {
        // macOS sun_path is 104 bytes; Windows AF_UNIX uses a similar limit.
        if (endpoint.Length >= 100)
        {
            throw new PathTooLongException(
                $"IPC endpoint path is too long for a Unix-domain socket: {endpoint}");
        }
    }
}