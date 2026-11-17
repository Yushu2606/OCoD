using System.Net.Sockets;

using OCoD.Protocol;

namespace OCoD.LibraryClient;

/// <summary>Starts and observes the single library daemon that serves one character library.</summary>
public sealed class LibraryDaemonSupervisor : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly string _libraryPath;
    private readonly Action<string>? _log;
    private readonly object _gate = new();

    private ManagedProcess? _process;
    private HelloResponse? _server;
    private bool _disposed;

    public LibraryDaemonSupervisor(string libraryPath, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        _libraryPath = Path.GetFullPath(libraryPath);
        _log = log;
    }

    public string LibraryPath => _libraryPath;

    public async Task<HelloResponse> EnsureStartedAsync(
        TimeSpan startupTimeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (await TryQueryHelloAsync(cancellationToken).ConfigureAwait(false) is { } existing)
        {
            SetServer(existing);
            return existing;
        }

        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                // Another caller is already waiting for this process.
            }
            else
            {
                var command = ComponentLocator.Resolve("OCoD.LibraryDaemon");
                _process = ManagedProcess.Start(
                    command,
                    ["--library", _libraryPath],
                    line => _log?.Invoke($"[library-daemon] {line}"));
                _process.Exited += OnDaemonExited;
                _log?.Invoke(
                    $"library daemon starting for '{_libraryPath}' (pid {_process.Pid})");
            }
        }

        var deadline = DateTimeOffset.UtcNow + startupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await TryQueryHelloAsync(cancellationToken).ConfigureAwait(false) is { } started)
            {
                SetServer(started);
                return started;
            }

            ManagedProcess? process;
            lock (_gate)
            {
                process = _process;
            }

            if (process is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"Library daemon exited with code {process.ExitCode} before it became ready.");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Library daemon for '{_libraryPath}' did not become ready within {startupTimeout}.");
    }

    public async Task<LibraryStatus?> QueryStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = await LibraryClient.ConnectAsync(
                _libraryPath,
                ComponentNames.ManagementWindow,
                timeout: TimeSpan.FromSeconds(2),
                cancellationToken).ConfigureAwait(false);

            SetServer(client.Server);
            return await client.GetStatusAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException
                or SocketException
                or IpcException
                or TimeoutException
                or OperationCanceledException)
        {
            return null;
        }
    }

    public ProcessInfo GetProcessInfo()
    {
        HelloResponse? server;
        ManagedProcess? process;

        lock (_gate)
        {
            server = _server;
            process = _process;
        }

        var running = process is { HasExited: false } || server is not null;

        return new ProcessInfo(
            "library-daemon",
            running,
            server?.Pid ?? (process is { HasExited: false } ? process.Pid : null),
            server?.Version,
            running ? null : "not running");
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ManagedProcess? process;
        lock (_gate)
        {
            process = _process;
        }

        try
        {
            await using var client = await LibraryClient.ConnectAsync(
                _libraryPath,
                ComponentNames.GlobalDaemon,
                timeout: TimeSpan.FromSeconds(2),
                cancellationToken).ConfigureAwait(false);

            await client.RequestShutdownAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException
                or SocketException
                or IpcException
                or TimeoutException
                or OperationCanceledException)
        {
        }

        if (process is not null)
        {
            if (!await process.WaitForExitAsync(TimeSpan.FromSeconds(3), cancellationToken)
                    .ConfigureAwait(false))
            {
                process.Kill();
            }

            await process.DisposeAsync().ConfigureAwait(false);
        }

        ClearProcess();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ManagedProcess? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }

        if (process is not null)
        {
            process.Exited -= OnDaemonExited;
            await process.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<HelloResponse?> TryQueryHelloAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var client = await LibraryClient.ConnectAsync(
                _libraryPath,
                ComponentNames.GlobalDaemon,
                timeout: TimeSpan.FromMilliseconds(500),
                cancellationToken).ConfigureAwait(false);

            return client.Server;
        }
        catch (Exception exception) when (
            exception is IOException
                or SocketException
                or IpcException
                or TimeoutException
                or OperationCanceledException)
        {
            return null;
        }
    }

    private void OnDaemonExited(int exitCode)
    {
        _log?.Invoke($"library daemon exited with code {exitCode}");
        ManagedProcess? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
            _server = null;
        }

        if (process is not null)
        {
            process.Exited -= OnDaemonExited;
        }
    }

    private void SetServer(HelloResponse server)
    {
        lock (_gate)
        {
            _server = server;
        }
    }

    private void ClearProcess()
    {
        lock (_gate)
        {
            if (_process is not null)
            {
                _process.Exited -= OnDaemonExited;
                _process = null;
            }

            _server = null;
        }
    }
}