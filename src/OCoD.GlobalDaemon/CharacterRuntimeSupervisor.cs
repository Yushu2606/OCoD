using System.Security.Cryptography;

using OCoD.Localization;
using OCoD.Protocol;

namespace OCoD.GlobalDaemon;

/// <summary>Launches one character runtime process and owns its control channel.</summary>
internal sealed class CharacterRuntimeSupervisor : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly string _libraryPath;
    private readonly string? _characterId;
    private readonly string? _characterName;
    private readonly string? _componentsDirectory;
    private readonly LocalizationService _localization;
    private readonly Action<string> _log;
    private readonly object _gate = new();

    private ManagedProcess? _process;
    private CharacterControlServer? _control;
    private CharacterState? _state;
    private bool _disposed;

    public CharacterRuntimeSupervisor(
        string libraryPath,
        string? characterId,
        string? characterName,
        string? componentsDirectory,
        LocalizationService localization,
        Action<string> log)
    {
        _libraryPath = libraryPath;
        _characterId = characterId;
        _characterName = characterName;
        _componentsDirectory = componentsDirectory;
        _localization = localization;
        _log = log;
    }

    public event Action? StateChanged;

    public string? CharacterId => _characterId;

    public string? CharacterName => _characterName;

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

    public ProcessInfo GetProcessInfo()
    {
        ManagedProcess? process;
        CharacterControlServer? control;
        CharacterState? state;

        lock (_gate)
        {
            process = _process;
            control = _control;
            state = _state;
        }

        var running = process is { HasExited: false };
        return new ProcessInfo(
            ComponentNames.CharacterRuntime,
            running,
            running ? process?.Pid : null,
            state?.Version,
            running && control is { IsConnected: false } ? "starting" : null);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ManagedProcess? process;
        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                return;
            }

            process = _process;
            _process = null;
            _control = null;
        }

        if (process is not null)
        {
            process.Exited -= OnProcessExited;
            await process.DisposeAsync().ConfigureAwait(false);
        }

        var endpoint = EndpointPaths.NewCharacterControlEndpoint();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var control = CharacterControlServer.Start(
            endpoint,
            token,
            message => _log($"[character-control] {message}"));
        control.StateChanged += OnStateChanged;
        control.RuntimeDisconnected += OnRuntimeDisconnected;

        var command = ComponentLocator.Resolve("OCoD.CharacterHost", _componentsDirectory);
        var arguments = new List<string>
        {
            "--library",
            _libraryPath,
            "--control-endpoint",
            endpoint,
            "--control-token",
            token,
            "--lang",
            _localization.Culture.Name,
        };

        if (_characterId is not null)
        {
            arguments.Add("--character");
            arguments.Add(_characterId);
        }

        ManagedProcess started;

        try
        {
            started = ManagedProcess.Start(
                command,
                arguments,
                line => _log($"[character-runtime] {line}"));
        }
        catch
        {
            control.StateChanged -= OnStateChanged;
            control.RuntimeDisconnected -= OnRuntimeDisconnected;
            await control.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        started.Exited += OnProcessExited;

        lock (_gate)
        {
            _process = started;
            _control = control;
        }

        try
        {
            await control.Connected.WaitAsync(StartupTimeout, cancellationToken)
                .ConfigureAwait(false);
            _log($"character runtime ready (pid {started.Pid})");
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ManagedProcess? process;
        CharacterControlServer? control;

        lock (_gate)
        {
            process = _process;
            control = _control;
            _process = null;
            _control = null;
            _state = null;
        }

        if (control is not null)
        {
            try
            {
                await control.SendCommandAsync(
                    new CharacterCommand(CharacterCommands.Shutdown, null),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                    or IpcException
                    or ObjectDisposedException
                    or OperationCanceledException)
            {
            }
        }

        if (process is not null)
        {
            if (!await process.WaitForExitAsync(ShutdownTimeout, cancellationToken)
                    .ConfigureAwait(false))
            {
                process.Kill();
            }

            process.Exited -= OnProcessExited;
            await process.DisposeAsync().ConfigureAwait(false);
        }

        if (control is not null)
        {
            control.StateChanged -= OnStateChanged;
            control.RuntimeDisconnected -= OnRuntimeDisconnected;
            await control.DisposeAsync().ConfigureAwait(false);
        }

        StateChanged?.Invoke();
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetClickThroughAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        CharacterControlServer? control;
        lock (_gate)
        {
            control = _control;
        }

        if (control is null)
        {
            throw new IpcException("The character runtime is not running.");
        }

        await control.SendCommandAsync(
            new CharacterCommand(CharacterCommands.SetClickThrough, enabled),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SetLanguageAsync(
        string languageTag,
        CancellationToken cancellationToken = default)
    {
        CharacterControlServer? control;
        lock (_gate)
        {
            control = _control;
        }

        if (control is null)
        {
            return;
        }

        await control.SendCommandAsync(
            new CharacterCommand(CharacterCommands.SetLanguage, null, languageTag),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        CharacterControlServer? control;
        lock (_gate)
        {
            control = _control;
        }

        if (control is not null)
        {
            await control.SendCommandAsync(
                new CharacterCommand(CharacterCommands.Show, null),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void OnStateChanged(CharacterState state)
    {
        lock (_gate)
        {
            _state = state;
        }

        StateChanged?.Invoke();
    }

    private void OnRuntimeDisconnected()
    {
        lock (_gate)
        {
            _state = null;
        }

        StateChanged?.Invoke();
    }

    private void OnProcessExited(int exitCode)
    {
        CharacterControlServer? control;
        ManagedProcess? process;

        lock (_gate)
        {
            process = _process;
            _process = null;
            _state = null;
            control = _control;
            _control = null;
        }

        if (process is not null)
        {
            process.Exited -= OnProcessExited;
            _ = process.DisposeAsync().AsTask();
        }

        if (control is not null)
        {
            control.StateChanged -= OnStateChanged;
            control.RuntimeDisconnected -= OnRuntimeDisconnected;
            _ = DisposeControlAsync(control);
        }

        _log($"character runtime exited with code {exitCode}");
        StateChanged?.Invoke();
    }

    private async Task DisposeControlAsync(CharacterControlServer control)
    {
        try
        {
            await control.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _log($"character control cleanup failed: {exception.Message}");
        }
    }
}