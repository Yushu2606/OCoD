using System.Diagnostics;

namespace OCoD.Protocol;

/// <summary>A child component process with redirected logs and a single exit notification.</summary>
public sealed class ManagedProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Action<string>? _log;
    private readonly Task _watchTask;
    private int _exitNotified;
    private int _disposed;

    private ManagedProcess(Process process, Action<string>? log)
    {
        _process = process;
        _log = log;
        _ = PumpAsync(process.StandardOutput, "stdout");
        _ = PumpAsync(process.StandardError, "stderr");
        _watchTask = WatchAsync();
    }

    public event Action<int>? Exited;

    public int Pid => _process.Id;

    public bool HasExited => _process.HasExited;

    public int? ExitCode
    {
        get
        {
            try
            {
                return _process.HasExited ? _process.ExitCode : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public static ManagedProcess Start(
        ComponentCommand command,
        IReadOnlyList<string> arguments,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
        };

        process.Start();

        log?.Invoke($"started {Path.GetFileName(command.FileName)} as pid {process.Id}");

        return new ManagedProcess(process, log);
    }

    public async Task<bool> WaitForExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await _process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Kill();

        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }

        await _watchTask.ConfigureAwait(false);
        _process.Dispose();
    }

    private async Task PumpAsync(StreamReader reader, string streamName)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                _log?.Invoke($"[{Pid}:{streamName}] {line}");
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private async Task WatchAsync()
    {
        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }

        if (Interlocked.Exchange(ref _exitNotified, 1) == 0)
        {
            var exitCode = ExitCode ?? -1;
            _log?.Invoke($"pid {Pid} exited with code {exitCode}");
            Exited?.Invoke(exitCode);
        }
    }
}