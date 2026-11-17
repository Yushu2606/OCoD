using OCoD.Protocol;

using LibraryClientApi = OCoD.LibraryClient.LibraryClient;

namespace OCoD.CharacterHost;

/// <summary>
/// Keeps the character runtime connected to its library daemon and reconnects if the daemon is
/// restarted. Reads and writes must go through this client; the runtime never touches the library
/// directory directly.
/// </summary>
internal sealed class LibraryRuntimeConnection : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    private readonly string _libraryPath;
    private readonly Action<bool> _onConnectionChanged;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _loop;

    private LibraryClientApi? _client;
    private int _connected;

    public LibraryRuntimeConnection(string libraryPath, Action<bool> onConnectionChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        _libraryPath = Path.GetFullPath(libraryPath);
        _onConnectionChanged = onConnectionChanged;
        _loop = RunAsync();
    }

    public string LibraryPath => _libraryPath;

    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    public async Task WaitForConnectedAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsConnected)
            {
                return;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("The character runtime could not connect to the library daemon.");
    }

    public Task<CharacterRecord> GetCharacterAsync(
        string characterId,
        CancellationToken cancellationToken = default) =>
        RequireClient().GetCharacterAsync(
            characterId,
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken);

    public async Task<byte[]> GetCharacterImageAsync(
        string characterId,
        string slot,
        CancellationToken cancellationToken = default)
    {
        var response = await RequireClient().GetCharacterImageAsync(
            characterId,
            slot,
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);

        return Convert.FromBase64String(response.Base64);
    }

    public Task SaveSettingsAsync(
        string characterId,
        CharacterRunSettings settings,
        CancellationToken cancellationToken = default) =>
        RequireClient().SaveRunSettingsAsync(
            characterId,
            settings,
            timeout: TimeSpan.FromSeconds(5),
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();

        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        var client = Interlocked.Exchange(ref _client, null);
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        _lifetime.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            LibraryClientApi? client = null;

            try
            {
                client = await LibraryClientApi.ConnectAsync(
                    _libraryPath,
                    ComponentNames.CharacterRuntime,
                    timeout: RequestTimeout,
                    _lifetime.Token).ConfigureAwait(false);

                Interlocked.Exchange(ref _client, client);
                SetConnected(true);

                while (!_lifetime.IsCancellationRequested)
                {
                    await Task.Delay(KeepAliveInterval, _lifetime.Token).ConfigureAwait(false);
                    await client.GetStatusAsync(RequestTimeout, _lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                SetConnected(false);
            }
            finally
            {
                Interlocked.CompareExchange(ref _client, null, client);
                if (client is not null)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }

                SetConnected(false);
            }

            try
            {
                await Task.Delay(ReconnectDelay, _lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private LibraryClientApi RequireClient() =>
        Volatile.Read(ref _client)
        ?? throw new IpcException("The character runtime is not connected to the library daemon.");

    private void SetConnected(bool connected)
    {
        var value = connected ? 1 : 0;
        if (Interlocked.Exchange(ref _connected, value) != value)
        {
            _onConnectionChanged(connected);
        }
    }
}