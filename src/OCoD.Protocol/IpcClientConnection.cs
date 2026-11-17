using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;

namespace OCoD.Protocol;

/// <summary>A persistent request/response connection with support for server-pushed events.</summary>
public sealed class IpcClientConnection : IAsyncDisposable
{
    private readonly JsonLineConnection _connection;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcMessage>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<IpcMessage, Task>? _onEvent;
    private readonly Task _readLoop;
    private long _nextRequestId;

    private IpcClientConnection(JsonLineConnection connection, Func<IpcMessage, Task>? onEvent)
    {
        _connection = connection;
        _onEvent = onEvent;
        _readLoop = ReadLoopAsync();
    }

    public event Action? Disconnected;

    public static async Task<IpcClientConnection> ConnectAsync(
        string endpoint,
        TimeSpan timeout,
        Func<IpcMessage, Task>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        EndpointPaths.ValidateEndpointLength(endpoint);
        EndpointPaths.EnsureSocketDirectory();

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new IpcClientConnection(
            new JsonLineConnection(new NetworkStream(socket, ownsSocket: true)),
            onEvent);
    }

    public Task SendEventAsync(
        string eventName,
        JsonElement? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        return _connection.WriteAsync(
            new IpcMessage
            {
                Event = eventName,
                Params = parameters,
            },
            cancellationToken).AsTask();
    }

    public async Task<IpcMessage> InvokeAsync(
        string method,
        JsonElement? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var id = Interlocked.Increment(ref _nextRequestId)
            .ToString(CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<IpcMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException($"Duplicate IPC request id '{id}'.");
        }

        try
        {
            await _connection.WriteAsync(
                new IpcMessage
                {
                    Id = id,
                    Method = method,
                    Params = parameters,
                },
                cancellationToken).ConfigureAwait(false);

            var response = timeout is { } responseTimeout
                ? await completion.Task.WaitAsync(responseTimeout, cancellationToken).ConfigureAwait(false)
                : await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (response.Ok == false)
            {
                throw new IpcException(response.Error ?? $"IPC call '{method}' failed.");
            }

            return response;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();

        await _connection.DisposeAsync().ConfigureAwait(false);

        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(new IpcException("IPC connection was closed."));
        }

        _pending.Clear();
        _lifetime.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var message = await _connection.ReadAsync(_lifetime.Token).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                if (message.Id is { } id && _pending.TryRemove(id, out var completion))
                {
                    completion.TrySetResult(message);
                    continue;
                }

                if (message.Event is not null && _onEvent is not null)
                {
                    await _onEvent(message).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(exception);
            }
        }
        finally
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new IpcException("IPC connection was closed."));
            }

            _pending.Clear();
            Disconnected?.Invoke();
        }
    }
}