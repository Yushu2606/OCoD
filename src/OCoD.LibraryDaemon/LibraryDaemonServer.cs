using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;

using OCoD.Protocol;

namespace OCoD.LibraryDaemon;

/// <summary>Owns the library socket, client sessions and idle-exit lifecycle.</summary>
internal sealed class LibraryDaemonServer : IAsyncDisposable
{
    private readonly LibraryStore _store;
    private readonly CharacterLibrary _characters;
    private readonly string _libraryPath;
    private readonly string _endpoint;
    private readonly TimeSpan _idleExitDelay;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<int, Session> _sessions = new();
    private readonly TaskCompletionSource<string> _shutdown =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IpcServerSocket? _server;
    private DateTimeOffset _startedAtUtc;
    private int _nextConnectionId;
    private int _activeClients;
    private int _stopping;

    public LibraryDaemonServer(
        LibraryStore store,
        CharacterLibrary characters,
        string libraryPath,
        string endpoint,
        TimeSpan idleExitDelay,
        Action<string> log)
    {
        _store = store;
        _characters = characters;
        _libraryPath = libraryPath;
        _endpoint = endpoint;
        _idleExitDelay = idleExitDelay;
        _log = log;
    }

    public Task<string> ShutdownRequested => _shutdown.Task;

    public void Start()
    {
        _startedAtUtc = DateTimeOffset.UtcNow;
        _server = IpcServerSocket.Start(
            _endpoint,
            HandleSessionAsync,
            exception => _log($"accept error: {exception.Message}"));
        _log($"listening on {_endpoint}");

        if (Volatile.Read(ref _activeClients) == 0)
        {
            _ = IdleExitAsync();
        }
    }

    public Task StopAsync(string reason) => StopCoreAsync(reason);

    public async ValueTask DisposeAsync()
    {
        await StopCoreAsync("disposing").ConfigureAwait(false);

        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
            _server = null;
        }
    }

    private async Task StopCoreAsync(string reason)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        _log($"stopping ({reason})");

        try
        {
            await _store.FlushAsync().ConfigureAwait(false);
            await _characters.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            _log($"flush failed: {exception.Message}");
        }

        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
            _server = null;
        }

        _shutdown.TrySetResult(reason);
    }

    private async Task HandleSessionAsync(Socket socket)
    {
        var connectionId = Interlocked.Increment(ref _nextConnectionId);
        var session = new Session(connectionId);
        _sessions[connectionId] = session;
        Interlocked.Increment(ref _activeClients);
        _log($"client {connectionId} connected");

        var connection = new JsonLineConnection(new NetworkStream(socket, ownsSocket: false));

        try
        {
            while (Volatile.Read(ref _stopping) == 0)
            {
                var request = await connection.ReadAsync().ConfigureAwait(false);
                if (request is null)
                {
                    break;
                }

                var response = await HandleRequestAsync(session, request).ConfigureAwait(false);
                if (request.Id is null)
                {
                    continue;
                }

                response.Id = request.Id;
                await connection.WriteAsync(response).ConfigureAwait(false);

                if (string.Equals(request.Method, "shutdown", StringComparison.Ordinal))
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                        await StopCoreAsync("client request").ConfigureAwait(false);
                    });
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or SocketException
                or ObjectDisposedException
                or OperationCanceledException
                or InvalidDataException)
        {
            _log($"client {connectionId} disconnected: {exception.Message}");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            _sessions.TryRemove(connectionId, out _);
            var remaining = Interlocked.Decrement(ref _activeClients);
            _log($"client {connectionId} left ({remaining} active)");

            if (remaining == 0 && Volatile.Read(ref _stopping) == 0)
            {
                _ = IdleExitAsync();
            }
        }
    }

    private async Task<IpcMessage> HandleRequestAsync(Session session, IpcMessage request)
    {
        try
        {
            switch (request.Method)
            {
                case "hello":
                    return HandleHello(session, request);
                case "status":
                    return Success(ProtocolJson.ToElement(await BuildStatusAsync().ConfigureAwait(false)));
                case "list-characters":
                    return Success(ProtocolJson.ToElement(
                        await _characters.ListCharactersAsync().ConfigureAwait(false)));
                case "import-cvclk":
                    if (request.Params is not { } importParameters)
                    {
                        return Failure("import-cvclk requires parameters.");
                    }

                    var importRequest = ProtocolJson.FromElement<ImportCvclkRequest>(importParameters);
                    var importResult = await _characters.ImportCvclkAsync(importRequest.Path)
                        .ConfigureAwait(false);
                    _log(
                        $"imported {importResult.Imported.Count} character(s), " +
                        $"skipped {importResult.Skipped.Count}");
                    return Success(ProtocolJson.ToElement(importResult));
                case "get-character":
                    if (request.Params is not { } getParameters)
                    {
                        return Failure("get-character requires parameters.");
                    }

                    var getRequest = ProtocolJson.FromElement<GetCharacterRequest>(getParameters);
                    var character = await _characters.GetCharacterAsync(getRequest.CharacterId)
                        .ConfigureAwait(false);

                    return character is null
                        ? Failure("Character was not found.")
                        : Success(ProtocolJson.ToElement(character));
                case "get-character-image":
                    if (request.Params is not { } imageParameters)
                    {
                        return Failure("get-character-image requires parameters.");
                    }

                    var imageRequest = ProtocolJson.FromElement<GetCharacterImageRequest>(imageParameters);
                    var image = await _characters.GetImageAsync(
                            imageRequest.CharacterId,
                            imageRequest.Slot)
                        .ConfigureAwait(false);

                    return image is null
                        ? Failure("Character image was not found.")
                        : Success(ProtocolJson.ToElement(image));
                case "save-settings":
                    if (request.Params is not { } settingsParameters)
                    {
                        return Failure("save-settings requires parameters.");
                    }

                    var settingsRequest = ProtocolJson.FromElement<SaveRunSettingsRequest>(settingsParameters);
                    await _characters.SaveSettingsAsync(
                            settingsRequest.CharacterId,
                            settingsRequest.Settings)
                        .ConfigureAwait(false);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));
                case "ping":
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));
                case "shutdown":
                    return Success(ProtocolJson.ToElement(new CommandResult(true, "shutting down")));
                default:
                    return Failure($"Unknown library daemon method '{request.Method}'.");
            }
        }
        catch (Exception exception)
        {
            _log($"request failed: {exception}");
            return Failure(exception.Message);
        }
    }

    private IpcMessage HandleHello(Session session, IpcMessage request)
    {
        if (request.Params is not { } parameters)
        {
            return Failure("hello requires parameters.");
        }

        var hello = ProtocolJson.FromElement<HelloRequest>(parameters);
        session.ClientName = string.IsNullOrWhiteSpace(hello.ClientName)
            ? "unknown"
            : hello.ClientName;
        session.Pid = hello.Pid;
        _log($"client {session.Id} hello from {session.ClientName} pid {session.Pid}");

        return Success(ProtocolJson.ToElement(new HelloResponse(
            ComponentNames.LibraryDaemon,
            Environment.ProcessId,
            ComponentVersion.Of(typeof(LibraryDaemonServer)),
            _libraryPath)));
    }

    private async Task<LibraryStatus> BuildStatusAsync()
    {
        var clients = _sessions.Values
            .OrderBy(session => session.Id)
            .Select(session => new LibraryClientInfo(
                session.Id,
                session.ClientName,
                session.Pid,
                session.ConnectedAtUtc))
            .ToList();

        return new LibraryStatus(
            LibraryPath: _libraryPath,
            LibraryId: _store.LibraryId,
            CharacterCount: await _characters.GetCharacterCountAsync().ConfigureAwait(false),
            DaemonPid: Environment.ProcessId,
            DaemonVersion: ComponentVersion.Of(typeof(LibraryDaemonServer)),
            StartedAtUtc: _startedAtUtc,
            PendingWrites: _store.PendingWrites + _characters.PendingWrites,
            Clients: clients);
    }

    private async Task IdleExitAsync()
    {
        await Task.Delay(_idleExitDelay).ConfigureAwait(false);

        if (Volatile.Read(ref _activeClients) == 0 && Volatile.Read(ref _stopping) == 0)
        {
            await StopCoreAsync("last client left").ConfigureAwait(false);
        }
    }

    private static IpcMessage Success(JsonElement result) => new()
    {
        Ok = true,
        Result = result,
    };

    private static IpcMessage Failure(string error) => new()
    {
        Ok = false,
        Error = error,
    };

    private sealed class Session(int id)
    {
        public int Id { get; } = id;
        public string ClientName { get; set; } = "unknown";
        public int Pid { get; set; }
        public DateTimeOffset ConnectedAtUtc { get; } = DateTimeOffset.UtcNow;
    }
}