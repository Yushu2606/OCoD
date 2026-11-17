using OCoD.Protocol;

namespace OCoD.Protocol.Tests;

public sealed class EndpointPathsTests
{
    [Fact]
    public void Library_endpoint_is_stable_and_fits_the_socket_limit()
    {
        var library = Path.Combine(Path.GetTempPath(), "ocod-endpoint-test", "library");

        var first = EndpointPaths.ForLibrary(library);
        var second = EndpointPaths.ForLibrary(library + Path.DirectorySeparatorChar);

        Assert.Equal(first, second);
        Assert.True(first.Length < 100, $"Endpoint is too long: {first}");
    }

    [Fact]
    public void Different_libraries_get_different_endpoints()
    {
        var first = EndpointPaths.ForLibrary("/tmp/ocod-library-a");
        var second = EndpointPaths.ForLibrary("/tmp/ocod-library-b");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Character_endpoints_are_unique_and_short()
    {
        var first = EndpointPaths.NewCharacterControlEndpoint();
        var second = EndpointPaths.NewCharacterControlEndpoint();

        Assert.NotEqual(first, second);
        Assert.True(first.Length < 100, $"Endpoint is too long: {first}");
        Assert.EndsWith(".sock", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_root_override_is_absolute()
    {
        var resolved = OcodPaths.ResolveRuntimeRoot("relative-runtime-root");

        Assert.True(Path.IsPathFullyQualified(resolved));
    }
}