using OCoD.Protocol;

namespace OCoD.LibraryClient;

/// <summary>A persistent client connection to one library daemon.</summary>
public sealed class LibraryClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly IpcClientConnection _connection;

    private LibraryClient(IpcClientConnection connection, HelloResponse server)
    {
        _connection = connection;
        Server = server;
    }

    public HelloResponse Server { get; }

    public static async Task<LibraryClient> ConnectAsync(
        string libraryPath,
        string clientName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);

        var effectiveTimeout = timeout ?? DefaultTimeout;
        var endpoint = EndpointPaths.ForLibrary(libraryPath);
        var connection = await IpcClientConnection.ConnectAsync(
            endpoint,
            effectiveTimeout,
            onEvent: null,
            cancellationToken).ConfigureAwait(false);

        try
        {
            var hello = new HelloRequest(
                clientName,
                Environment.ProcessId,
                ComponentVersion.Of(typeof(LibraryClient)));
            var response = await connection.InvokeAsync(
                "hello",
                ProtocolJson.ToElement(hello),
                effectiveTimeout,
                cancellationToken).ConfigureAwait(false);

            var server = response.Result is { } result
                ? ProtocolJson.FromElement<HelloResponse>(result)
                : throw new IpcException("Library daemon returned no hello response.");

            return new LibraryClient(connection, server);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<LibraryStatus> GetStatusAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _connection.InvokeAsync(
            "status",
            parameters: null,
            timeout ?? DefaultTimeout,
            cancellationToken).ConfigureAwait(false);

        return response.Result is { } result
            ? ProtocolJson.FromElement<LibraryStatus>(result)
            : throw new IpcException("Library daemon returned no status.");
    }

    public async Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _connection.InvokeAsync(
            "list-characters",
            parameters: null,
            timeout ?? DefaultTimeout,
            cancellationToken).ConfigureAwait(false);

        return response.Result is { } result
            ? ProtocolJson.FromElement<List<CharacterSummary>>(result)
            : [];
    }

    public async Task<CharacterRecord> GetCharacterAsync(
        string characterId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _connection.InvokeAsync(
            "get-character",
            ProtocolJson.ToElement(new GetCharacterRequest(characterId)),
            timeout ?? DefaultTimeout,
            cancellationToken).ConfigureAwait(false);

        return response.Result is { } result
            ? ProtocolJson.FromElement<CharacterRecord>(result)
            : throw new IpcException("Library daemon returned no character record.");
    }

    public async Task<CharacterImageResponse> GetCharacterImageAsync(
        string characterId,
        string slot,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _connection.InvokeAsync(
            "get-character-image",
            ProtocolJson.ToElement(new GetCharacterImageRequest(characterId, slot)),
            timeout ?? TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);

        return response.Result is { } result
            ? ProtocolJson.FromElement<CharacterImageResponse>(result)
            : throw new IpcException("Library daemon returned no character image.");
    }

    public async Task<CharacterImportResult> ImportCvclkAsync(
        string packagePath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _connection.InvokeAsync(
            "import-cvclk",
            ProtocolJson.ToElement(new ImportCvclkRequest(packagePath)),
            timeout ?? TimeSpan.FromMinutes(2),
            cancellationToken).ConfigureAwait(false);

        return response.Result is { } result
            ? ProtocolJson.FromElement<CharacterImportResult>(result)
            : throw new IpcException("Library daemon returned no import result.");
    }

    public async Task SaveRunSettingsAsync(
        string characterId,
        CharacterRunSettings settings,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        await _connection.InvokeAsync(
            "save-settings",
            ProtocolJson.ToElement(new SaveRunSettingsRequest(characterId, settings)),
            timeout ?? DefaultTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RequestShutdownAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        await _connection.InvokeAsync(
            "shutdown",
            parameters: null,
            timeout ?? DefaultTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}