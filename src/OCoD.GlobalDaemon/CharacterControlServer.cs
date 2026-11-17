using System.Net.Sockets;

using OCoD.Protocol;

namespace OCoD.GlobalDaemon;

/// <summary>
/// One authenticated control connection from the character runtime. The runtime pushes state events
/// and accepts commands sent by the global daemon.
/// </summary>
internal sealed class CharacterControlServer : IAsyncDisposable
{
    private readonly string _token;
    private readonly Action<string> _log;
    private readonly IpcServerSocket _server;
    private readonly TaskCompletionSource<bool> _connected =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    private JsonLineConnection? _connection;
    private CharacterState? _state;

    private CharacterControlServer(
        string endpoint,
        string token,
        Action<string> log)
    {
        _token = token;
        _log = log;
        _server = IpcServerSocket.Start(
            endpoint,
            HandleSessionAsync,
            exception => _log($"accept error: {exception.Message}"));
    }

    public event Action<CharacterState>? StateChanged;

    public event Action? RuntimeDisconnected;

    public Task Connected => _connected.Task;

    public CharacterState? State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _connection is not null;
            }
        }
    }

    public static CharacterControlServer Start(
        string endpoint,
        string token,
        Action<string> log) =>
        new(endpoint, token, log);

    public async Task SendCommandAsync(
        CharacterCommand command,
        CancellationToken cancellationToken = default)
    {
        JsonLineConnection? connection;

        lock (_gate)
        {
            connection = _connection;
        }

        if (connection is null)
        {
            throw new IpcException("The character runtime control channel is not connected.");
        }

        await connection.WriteAsync(
            new IpcMessage
            {
                Event = "command",
                Params = ProtocolJson.ToElement(command),
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync().ConfigureAwait(false);
        JsonLineConnection? connection;

        lock (_gate)
        {
            connection = _connection;
            _connection = null;
        }

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleSessionAsync(Socket socket)
    {
        var connection = new JsonLineConnection(new NetworkStream(socket, ownsSocket: false));
        var accepted = false;

        try
        {
            while (true)
            {
                var message = await connection.ReadAsync().ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                if (message.Id is { } id && string.Equals(message.Method, "hello", StringComparison.Ordinal))
                {
                    if (message.Params is not { } parameters)
                    {
                        await WriteResponseAsync(connection, id, false, "hello requires parameters.")
                            .ConfigureAwait(false);
                        break;
                    }

                    var hello = ProtocolJson.FromElement<CharacterHello>(parameters);
                    if (!string.Equals(hello.Token, _token, StringComparison.Ordinal))
                    {
                        _log("rejected character control connection with an invalid token");
                        await WriteResponseAsync(connection, id, false, "Invalid control token.")
                            .ConfigureAwait(false);
                        break;
                    }

                    lock (_gate)
                    {
                        if (_connection is not null)
                        {
                            accepted = false;
                            _log("rejected a second character control connection");
                        }
                        else
                        {
                            _connection = connection;
                            accepted = true;
                        }
                    }

                    if (!accepted)
                    {
                        await WriteResponseAsync(connection, id, false, "Character control already connected.")
                            .ConfigureAwait(false);
                        break;
                    }

                    _log(
                        $"character runtime connected (pid {hello.Pid}, version {hello.Version})");
                    _connected.TrySetResult(true);
                    await WriteResponseAsync(connection, id, true, null).ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(message.Event, "state", StringComparison.Ordinal)
                    && accepted
                    && message.Params is { } stateParameters)
                {
                    var state = ProtocolJson.FromElement<CharacterState>(stateParameters);
                    lock (_gate)
                    {
                        _state = state;
                    }

                    StateChanged?.Invoke(state);
                    continue;
                }

                if (message.Id is { } unknownId)
                {
                    await WriteResponseAsync(
                        connection,
                        unknownId,
                        false,
                        $"Unknown character control message '{message.Method ?? message.Event}'.")
                        .ConfigureAwait(false);
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
            _log($"character control session ended: {exception.Message}");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            if (accepted)
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_connection, connection))
                    {
                        _connection = null;
                    }
                }

                _log("character runtime control channel disconnected");
                RuntimeDisconnected?.Invoke();
            }
        }
    }

    private static Task WriteResponseAsync(
        JsonLineConnection connection,
        string id,
        bool ok,
        string? error) =>
        connection.WriteAsync(new IpcMessage
        {
            Id = id,
            Ok = ok,
            Error = error,
        }).AsTask();
}