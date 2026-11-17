using System.Net.Sockets;

using OCoD.Protocol;

namespace OCoD.GlobalDaemon;

/// <summary>Request/response channel used by the launcher and development tools.</summary>
internal sealed class GlobalDaemonControlServer : IAsyncDisposable
{
    private readonly IpcServerSocket _server;

    private GlobalDaemonControlServer(
        string endpoint,
        Func<IpcMessage, Task<IpcMessage>> handler,
        Action<string> log)
    {
        _server = IpcServerSocket.Start(
            endpoint,
            socket => HandleSessionAsync(socket, handler, log),
            exception => log($"accept error: {exception.Message}"));
    }

    public static GlobalDaemonControlServer Start(
        string endpoint,
        Func<IpcMessage, Task<IpcMessage>> handler,
        Action<string> log) =>
        new(endpoint, handler, log);

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    private static async Task HandleSessionAsync(
        Socket socket,
        Func<IpcMessage, Task<IpcMessage>> handler,
        Action<string> log)
    {
        var connection = new JsonLineConnection(new NetworkStream(socket, ownsSocket: false));

        try
        {
            while (true)
            {
                var request = await connection.ReadAsync().ConfigureAwait(false);
                if (request is null)
                {
                    break;
                }

                if (request.Id is null)
                {
                    continue;
                }

                IpcMessage response;

                try
                {
                    response = await handler(request).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    log($"control request '{request.Method}' failed: {exception}");
                    response = new IpcMessage
                    {
                        Ok = false,
                        Error = exception.Message,
                    };
                }

                response.Id = request.Id;
                await connection.WriteAsync(response).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or SocketException
                or ObjectDisposedException
                or OperationCanceledException
                or InvalidDataException)
        {
            log($"control session ended: {exception.Message}");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}