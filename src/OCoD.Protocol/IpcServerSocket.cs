using System.Collections.Concurrent;
using System.Net.Sockets;

namespace OCoD.Protocol;

/// <summary>A Unix-domain-socket listener that hands each accepted socket to a session handler.</summary>
public sealed class IpcServerSocket : IAsyncDisposable
{
    private readonly Socket _listener;
    private readonly string _endpoint;
    private readonly Func<Socket, Task> _sessionHandler;
    private readonly Action<Exception>? _onError;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<Socket, byte> _clients = new();
    private readonly Task _acceptLoop;
    private bool _disposed;

    private IpcServerSocket(
        Socket listener,
        string endpoint,
        Func<Socket, Task> sessionHandler,
        Action<Exception>? onError)
    {
        _listener = listener;
        _endpoint = endpoint;
        _sessionHandler = sessionHandler;
        _onError = onError;
        _acceptLoop = AcceptLoopAsync();
    }

    public static IpcServerSocket Start(
        string endpoint,
        Func<Socket, Task> sessionHandler,
        Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(sessionHandler);

        EndpointPaths.ValidateEndpointLength(endpoint);
        EndpointPaths.EnsureSocketDirectory();

        try
        {
            File.Delete(endpoint);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(endpoint));
            listener.Listen(backlog: 16);
        }
        catch
        {
            listener.Dispose();
            throw;
        }

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(endpoint, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        return new IpcServerSocket(listener, endpoint, sessionHandler, onError);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _listener.Dispose();

        foreach (var client in _clients.Keys)
        {
            try
            {
                client.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            File.Delete(_endpoint);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        _lifetime.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            Socket client;

            try
            {
                client = await _listener.AcceptAsync(_lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException exception)
            {
                _onError?.Invoke(exception);
                continue;
            }

            _clients.TryAdd(client, 0);
            _ = Task.Run(async () =>
            {
                try
                {
                    await _sessionHandler(client).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _onError?.Invoke(exception);
                }
                finally
                {
                    _clients.TryRemove(client, out _);
                    client.Dispose();
                }
            });
        }
    }
}