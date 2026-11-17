using System.Text.Json;

using OCoD.Protocol;

namespace OCoD.CharacterHost;

/// <summary>
/// The character runtime's control channel to the global daemon. The runtime reports its state and
/// accepts commands such as restoring interaction after click-through.
/// </summary>
internal sealed class CharacterControlClient : IAsyncDisposable
{
    private readonly IpcClientConnection _connection;

    private CharacterControlClient(IpcClientConnection connection)
    {
        _connection = connection;
    }

    public static async Task<CharacterControlClient> ConnectAsync(
        string endpoint,
        string token,
        Action<CharacterCommand> onCommand,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onCommand);

        var connection = await IpcClientConnection.ConnectAsync(
            endpoint,
            TimeSpan.FromSeconds(5),
            message =>
            {
                if (string.Equals(message.Event, "command", StringComparison.Ordinal)
                    && message.Params is { } parameters)
                {
                    var command = ProtocolJson.FromElement<CharacterCommand>(parameters);
                    onCommand(command);
                }

                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        try
        {
            var hello = new CharacterHello(
                token,
                Environment.ProcessId,
                ComponentVersion.Of(typeof(CharacterControlClient)));
            var response = await connection.InvokeAsync(
                "hello",
                ProtocolJson.ToElement(hello),
                TimeSpan.FromSeconds(5),
                cancellationToken).ConfigureAwait(false);

            if (response.Ok != true)
            {
                throw new IpcException(response.Error ?? "Character control handshake failed.");
            }

            return new CharacterControlClient(connection);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task PublishStateAsync(CharacterState state)
    {
        var parameters = ProtocolJson.ToElement(state);
        return PublishStateCoreAsync(parameters);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private Task PublishStateCoreAsync(JsonElement parameters) =>
        _connection.SendEventAsync("state", parameters);
}