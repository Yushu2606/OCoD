using Avalonia.Controls;

using OCoD.LibraryClient;
using OCoD.Localization;
using OCoD.Protocol;

using LibraryClientApi = OCoD.LibraryClient.LibraryClient;

namespace OCoD.GlobalDaemon;

/// <summary>
/// One character library and everything the global daemon owns for it: its library daemon,
/// character runtimes, management window and topology gate.
/// </summary>
internal sealed class LibraryRuntimeHost : IAsyncDisposable
{
    private const string PlaceholderRuntimeKey = "__placeholder";

    private static readonly TimeSpan LibraryStartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ManagementStartupTimeout = TimeSpan.FromSeconds(10);

    private readonly string _displayName;
    private readonly string _runtimeRoot;
    private readonly string? _componentsDirectory;
    private readonly LocalizationService _localization;
    private readonly WindowIcon _icon;
    private readonly Action<string> _log;
    private readonly LibraryDaemonSupervisor _libraryDaemon;
    private readonly Dictionary<string, CharacterRuntimeSupervisor> _runtimes =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _topologyGate = new(1, 1);
    private readonly CancellationToken _lifetimeToken;

    private ManagementWindow? _managementWindow;
    private int _disposed;

    public LibraryRuntimeHost(
        string libraryPath,
        string displayName,
        string runtimeRoot,
        string? componentsDirectory,
        LocalizationService localization,
        WindowIcon icon,
        CancellationToken lifetimeToken,
        Action<string> log)
    {
        LibraryPath = NormalizeLibraryPath(libraryPath);
        _displayName = displayName;
        _runtimeRoot = runtimeRoot;
        _componentsDirectory = componentsDirectory;
        _localization = localization;
        _icon = icon;
        _lifetimeToken = lifetimeToken;
        _log = log;
        _libraryDaemon = new LibraryDaemonSupervisor(LibraryPath, log);
    }

    public event Action? Changed;

    public string LibraryPath { get; }

    public string DisplayName => _displayName;

    public bool ManagementWindowOpen => _managementWindow is { IsVisible: true };

    public ProcessInfo LibraryDaemonProcess => _libraryDaemon.GetProcessInfo();

    public IReadOnlyList<CharacterRuntimeStatus> CharacterRuntimes =>
        _runtimes.Values
            .Select(runtime => new CharacterRuntimeStatus(
                runtime.CharacterId,
                runtime.CharacterName,
                runtime.GetProcessInfo(),
                runtime.State))
            .ToList();

    public static string NormalizeLibraryPath(string libraryPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryPath));

    public static string GetDefaultDisplayName(string libraryPath)
    {
        var trimmed = NormalizeLibraryPath(libraryPath);
        var name = Path.GetFileName(trimmed);

        if (string.IsNullOrWhiteSpace(name))
        {
            return trimmed;
        }

        // Windows portable libraries are always <distribution>/OCoDData/Library. Use the
        // distribution directory so two distributions do not both appear as "Library".
        var parent = Path.GetDirectoryName(trimmed);
        if (string.Equals(name, "Library", StringComparison.OrdinalIgnoreCase)
            && parent is not null
            && string.Equals(
                Path.GetFileName(parent),
                "OCoDData",
                StringComparison.OrdinalIgnoreCase))
        {
            var distribution = Path.GetFileName(Path.GetDirectoryName(parent));
            if (!string.IsNullOrWhiteSpace(distribution))
            {
                return distribution;
            }
        }

        return name;
    }

    public GlobalStatus GetStatus()
    {
        var runtimeStatuses = CharacterRuntimes.ToList();
        var primary = runtimeStatuses.FirstOrDefault();

        return new GlobalStatus(
            Environment.ProcessId,
            ComponentVersion.Of(typeof(GlobalDaemonService)),
            _runtimeRoot,
            LibraryPath,
            ManagementWindowOpen,
            _libraryDaemon.GetProcessInfo(),
            primary?.Process ?? new ProcessInfo(
                ComponentNames.CharacterRuntime,
                Running: false,
                Pid: null,
                Version: null,
                Detail: "not running"),
            primary?.State,
            runtimeStatuses);
    }

    public LibraryTopologyStatus GetLibraryStatus() => new(
        LibraryPath,
        DisplayName,
        ManagementWindowOpen,
        _libraryDaemon.GetProcessInfo(),
        CharacterRuntimes.ToList());

    public Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync() => QueryCharactersAsync();

    public async Task StartAsync(bool startPlaceholderWhenEmpty)
    {
        await WithTopologyGateAsync(async () =>
        {
            _log($"topology starting for '{LibraryPath}'");
            await _libraryDaemon.EnsureStartedAsync(
                LibraryStartupTimeout,
                _lifetimeToken).ConfigureAwait(true);

            var characters = await QueryCharactersAsync().ConfigureAwait(true);

            if (characters.Count == 0 && startPlaceholderWhenEmpty)
            {
                await StartPlaceholderWithLeaseAsync().ConfigureAwait(true);
            }
            else if (characters.Count > 0)
            {
                _log($"library has {characters.Count} character(s); waiting for an explicit start");
            }
        }).ConfigureAwait(true);
    }

    public async Task EnsureLibraryDaemonAsync()
    {
        await WithTopologyGateAsync(async () =>
        {
            await _libraryDaemon.EnsureStartedAsync(
                LibraryStartupTimeout,
                _lifetimeToken).ConfigureAwait(true);
        }).ConfigureAwait(true);

        Changed?.Invoke();
    }

    public async Task EnsureRuntimeAsync()
    {
        await WithTopologyGateAsync(EnsureRuntimeCoreAsync).ConfigureAwait(true);
        Changed?.Invoke();
    }

    public async Task StartCharacterAsync(string characterId)
    {
        await WithTopologyGateAsync(async () =>
        {
            await _libraryDaemon.EnsureStartedAsync(
                LibraryStartupTimeout,
                _lifetimeToken).ConfigureAwait(true);
            await StartCharacterCoreAsync(characterId, characterName: null).ConfigureAwait(true);
        }).ConfigureAwait(true);

        Changed?.Invoke();
    }

    public async Task StopCharacterAsync(string characterId)
    {
        await WithTopologyGateAsync(
            () => RemoveRuntimeAsync(characterId)).ConfigureAwait(true);
        Changed?.Invoke();
    }

    public async Task ActivateCharacterAsync(string characterId)
    {
        if (_runtimes.TryGetValue(characterId, out var runtime))
        {
            await runtime.ShowAsync(_lifetimeToken).ConfigureAwait(true);
        }
    }

    public async Task StopAllRuntimesAsync()
    {
        await WithTopologyGateAsync(async () =>
        {
            foreach (var key in _runtimes.Keys.ToList())
            {
                await RemoveRuntimeAsync(key).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);

        Changed?.Invoke();
    }

    public async Task RestartAllRuntimesAsync()
    {
        await WithTopologyGateAsync(async () =>
        {
            foreach (var key in _runtimes.Keys.ToList())
            {
                await RemoveRuntimeAsync(key).ConfigureAwait(true);
            }

            await EnsureRuntimeCoreAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);

        Changed?.Invoke();
    }

    public async Task RestartLibraryDaemonAsync()
    {
        _log($"restarting library daemon for '{LibraryPath}'");
        await WithTopologyGateAsync(async () =>
        {
            await _libraryDaemon.ShutdownAsync(_lifetimeToken).ConfigureAwait(true);
            await _libraryDaemon.EnsureStartedAsync(
                LibraryStartupTimeout,
                _lifetimeToken).ConfigureAwait(true);
        }).ConfigureAwait(true);

        _log($"library daemon restarted for '{LibraryPath}'");
        Changed?.Invoke();
    }

    public async Task SetClickThroughAsync(bool enabled)
    {
        foreach (var runtime in _runtimes.Values.ToList())
        {
            try
            {
                await runtime.SetClickThroughAsync(enabled, _lifetimeToken).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _log($"could not change click-through in '{LibraryPath}': {exception.Message}");
            }
        }
    }

    public async Task SetLanguageAsync(string languageTag)
    {
        foreach (var runtime in _runtimes.Values.ToList())
        {
            try
            {
                await runtime.SetLanguageAsync(languageTag, _lifetimeToken).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _log(
                    $"could not change the character runtime language in '{LibraryPath}': " +
                    exception.Message);
            }
        }
    }

    public async Task OpenManagementWindowAsync()
    {
        if (_managementWindow is { } existing)
        {
            if (!existing.IsVisible)
            {
                existing.Show();
            }

            existing.Activate();
            Changed?.Invoke();
            return;
        }

        try
        {
            await WithTopologyGateAsync(async () =>
            {
                await _libraryDaemon.EnsureStartedAsync(
                    ManagementStartupTimeout,
                    _lifetimeToken).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log(
                $"could not ensure the library daemon for the management window in " +
                $"'{LibraryPath}': {exception.Message}");
        }

        var window = new ManagementWindow(
            _localization,
            _icon,
            LibraryPath,
            DisplayName,
            () => Task.FromResult(GetStatus()),
            StartCharacterAsync,
            StopCharacterAsync,
            RestartLibraryDaemonAsync,
            () => SetClickThroughAsync(false),
            message => _log($"[management:{DisplayName}] {message}"));

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_managementWindow, window))
            {
                _managementWindow = null;
                Changed?.Invoke();
            }
        };

        _managementWindow = window;
        window.Show();
        window.Activate();
        Changed?.Invoke();
    }

    public void CloseManagementWindow()
    {
        if (_managementWindow is { } window)
        {
            window.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_managementWindow is { } window)
        {
            _managementWindow = null;
            window.Close();
        }

        foreach (var key in _runtimes.Keys.ToList())
        {
            await RemoveRuntimeAsync(key).ConfigureAwait(true);
        }

        try
        {
            await _libraryDaemon.ShutdownAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log($"library daemon cleanup failed for '{LibraryPath}': {exception.Message}");
        }

        try
        {
            await _libraryDaemon.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log($"library supervisor cleanup failed for '{LibraryPath}': {exception.Message}");
        }
    }

    private async Task EnsureRuntimeCoreAsync()
    {
        await _libraryDaemon.EnsureStartedAsync(
            LibraryStartupTimeout,
            _lifetimeToken).ConfigureAwait(true);

        var characters = await QueryCharactersAsync().ConfigureAwait(true);

        if (characters.Count == 0)
        {
            await StartPlaceholderWithLeaseAsync().ConfigureAwait(true);
            return;
        }

        foreach (var character in characters)
        {
            await StartCharacterCoreAsync(character.Id, character.Name).ConfigureAwait(true);
        }
    }

    private async Task StartCharacterCoreAsync(string characterId, string? characterName)
    {
        if (_runtimes.TryGetValue(characterId, out var existing))
        {
            if (existing.GetProcessInfo().Running)
            {
                await existing.ShowAsync(_lifetimeToken).ConfigureAwait(true);
                return;
            }

            await RemoveRuntimeAsync(characterId).ConfigureAwait(true);
        }

        await StopPlaceholderAsync().ConfigureAwait(true);

        var record = await GetCharacterRecordAsync(characterId).ConfigureAwait(true)
            ?? throw new IpcException($"Character '{characterId}' was not found.");
        var runtime = CreateRuntime(characterId, record.Name);

        try
        {
            await StartSupervisorWithLeaseAsync(runtime).ConfigureAwait(true);
        }
        catch
        {
            await RemoveRuntimeAsync(characterId).ConfigureAwait(true);
            throw;
        }
    }

    private async Task StartPlaceholderWithLeaseAsync()
    {
        if (_runtimes.TryGetValue(PlaceholderRuntimeKey, out var existing)
            && existing.GetProcessInfo().Running)
        {
            return;
        }

        await RemoveRuntimeAsync(PlaceholderRuntimeKey).ConfigureAwait(true);
        var runtime = CreateRuntime(characterId: null, characterName: null);

        try
        {
            await StartSupervisorWithLeaseAsync(runtime).ConfigureAwait(true);
        }
        catch
        {
            await RemoveRuntimeAsync(PlaceholderRuntimeKey).ConfigureAwait(true);
            throw;
        }
    }

    private async Task StartSupervisorWithLeaseAsync(CharacterRuntimeSupervisor runtime)
    {
        await _libraryDaemon.EnsureStartedAsync(
            LibraryStartupTimeout,
            _lifetimeToken).ConfigureAwait(true);

        // Keep the library daemon alive while Avalonia and the control channel start up in the
        // character runtime; its own library client connects shortly after the control handshake.
        await using var lease = await LibraryClientApi.ConnectAsync(
            LibraryPath,
            ComponentNames.GlobalDaemon,
            timeout: TimeSpan.FromSeconds(5),
            _lifetimeToken).ConfigureAwait(true);

        await runtime.StartAsync(_lifetimeToken).ConfigureAwait(true);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (runtime.State?.LibraryConnected == true)
            {
                return;
            }

            await Task.Delay(100, _lifetimeToken).ConfigureAwait(true);
        }

        _log($"warning: a character runtime in '{LibraryPath}' did not report a library connection during startup");
    }

    private async Task StopPlaceholderAsync()
    {
        if (_runtimes.ContainsKey(PlaceholderRuntimeKey))
        {
            await RemoveRuntimeAsync(PlaceholderRuntimeKey).ConfigureAwait(true);
        }
    }

    private CharacterRuntimeSupervisor CreateRuntime(string? characterId, string? characterName)
    {
        var runtime = new CharacterRuntimeSupervisor(
            LibraryPath,
            characterId,
            characterName,
            _componentsDirectory,
            _localization,
            _log);
        runtime.StateChanged += OnRuntimeStateChanged;

        var key = characterId ?? PlaceholderRuntimeKey;
        _runtimes[key] = runtime;
        return runtime;
    }

    private async Task RemoveRuntimeAsync(string key)
    {
        if (!_runtimes.Remove(key, out var runtime))
        {
            return;
        }

        runtime.StateChanged -= OnRuntimeStateChanged;

        try
        {
            await runtime.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log($"character runtime cleanup failed in '{LibraryPath}': {exception.Message}");
        }
    }

    private async Task WithTopologyGateAsync(Func<Task> action)
    {
        await _topologyGate.WaitAsync(_lifetimeToken).ConfigureAwait(true);
        try
        {
            await action().ConfigureAwait(true);
        }
        finally
        {
            _topologyGate.Release();
        }
    }

    private async Task<IReadOnlyList<CharacterSummary>> QueryCharactersAsync()
    {
        try
        {
            await using var client = await LibraryClientApi.ConnectAsync(
                LibraryPath,
                ComponentNames.GlobalDaemon,
                timeout: TimeSpan.FromSeconds(5),
                _lifetimeToken).ConfigureAwait(true);

            return await client.ListCharactersAsync(
                timeout: TimeSpan.FromSeconds(5),
                _lifetimeToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is IOException
                or System.Net.Sockets.SocketException
                or IpcException
                or TimeoutException
                or OperationCanceledException)
        {
            _log($"could not list characters in '{LibraryPath}': {exception.Message}");
            return [];
        }
    }

    private async Task<CharacterRecord?> GetCharacterRecordAsync(string characterId)
    {
        try
        {
            await using var client = await LibraryClientApi.ConnectAsync(
                LibraryPath,
                ComponentNames.GlobalDaemon,
                timeout: TimeSpan.FromSeconds(5),
                _lifetimeToken).ConfigureAwait(true);

            return await client.GetCharacterAsync(
                characterId,
                timeout: TimeSpan.FromSeconds(5),
                _lifetimeToken).ConfigureAwait(true);
        }
        catch (IpcException)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is IOException
                or System.Net.Sockets.SocketException
                or TimeoutException
                or OperationCanceledException)
        {
            _log($"could not read character '{characterId}' in '{LibraryPath}': {exception.Message}");
            return null;
        }
    }

    private void OnRuntimeStateChanged() => Changed?.Invoke();
}