namespace OCoD.Protocol;

public sealed record ComponentCommand(string FileName, IReadOnlyList<string> Arguments);

/// <summary>
/// Finds a sibling component executable in a published distribution, or in the repository build
/// output during development. Pass OCOD_COMPONENTS_DIR to override both.
/// </summary>
public static class ComponentLocator
{
    public static ComponentCommand Resolve(string componentName, string? componentsDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);

        var configured = componentsDirectory;
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(
                OcodPaths.ComponentsDirectoryEnvironmentVariable);
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return ResolveIn(Path.GetFullPath(configured), componentName)
                ?? throw new FileNotFoundException(
                    $"Component '{componentName}' was not found in '{configured}'.");
        }

        var baseDirectory = AppContext.BaseDirectory;
        if (ResolveIn(baseDirectory, componentName) is { } sibling)
        {
            return sibling;
        }

        var repositoryRoot = FindRepositoryRoot(baseDirectory);
        if (repositoryRoot is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var buildDirectory = Path.Combine(
                    repositoryRoot,
                    "src",
                    componentName,
                    "bin",
                    configuration,
                    "net10.0");

                if (ResolveIn(buildDirectory, componentName) is { } build)
                {
                    return build;
                }
            }
        }

        throw new FileNotFoundException(
            $"Component '{componentName}' was not found. Publish the components beside each other " +
            $"or set {OcodPaths.ComponentsDirectoryEnvironmentVariable}.");
    }

    private static ComponentCommand? ResolveIn(string directory, string componentName)
    {
        var executableName = OperatingSystem.IsWindows()
            ? componentName + ".exe"
            : componentName;
        var executablePath = Path.Combine(directory, executableName);

        if (File.Exists(executablePath))
        {
            return new ComponentCommand(executablePath, []);
        }

        var assemblyPath = Path.Combine(directory, componentName + ".dll");
        if (File.Exists(assemblyPath))
        {
            return new ComponentCommand("dotnet", [assemblyPath]);
        }

        return null;
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OCoD.slnx"))
                || File.Exists(Path.Combine(directory.FullName, "OCoD.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}