using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using OCoD.Desktop;
using OCoD.LibraryClient;
using OCoD.Localization;
using OCoD.Protocol;

using LibraryClientApi = OCoD.LibraryClient.LibraryClient;

namespace OCoD.GlobalDaemon;

/// <summary>
/// Owns the tray entry, the management window, the library daemon and the character runtime.
/// The global daemon is the only component that starts the others in the development topology.
/// </summary>
internal sealed class GlobalDaemonService : IAsyncDisposable
{
    private const string PlaceholderRuntimeKey = "__placeholder";

    private static readonly TimeSpan LibraryStartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ManagementStartupTimeout = TimeSpan.FromSeconds(10);

    private readonly GlobalDaemonOptions _options;
    private readonly LocalizationService _localization;
    private readonly ClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly Action<string> _log;
    private readonly WindowIcon _icon;
    private readonly LibraryDaemonSupervisor _libraryDaemon;
    private readonly Dictionary<string, CharacterRuntimeSupervisor> _runtimes =
        new(StringComparer.Ordinal);
    private readonly GlobalDaemonControlServer _controlServer;
    private readonly NativeMenu _trayMenu;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _openManagementItem;
    private readonly NativeMenuItem _clickThroughItem;
    private readonly NativeMenuItem _languageItem;
    private readonly NativeMenuItem _exitItem;
    private readonly Dictionary<string, NativeMenuItem> _languageItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly SemaphoreSlim _topologyGate = new(1, 1);

    private ManagementWindow? _managementWindow;
    private Task? _startupTask;
    private int _shutdownRequested;
    private int _disposed;
    private string? _startupError;

    public GlobalDaemonService(
        GlobalDaemonOptions options,
        LocalizationService localization,
        ClassicDesktopStyleApplicationLifetime lifetime,
        Action<string> log)
    {
        _options = options;
        _localization = localization;
        _lifetime = lifetime;
        _log = log;
        _icon = DesktopAssets.LoadIcon();

        _libraryDaemon = new LibraryDaemonSupervisor(options.LibraryPath, log);

        _openManagementItem = new NativeMenuItem();
        _openManagementItem.Click += (_, _) => _ = OpenManagementWindowAsync();

        _clickThroughItem = new NativeMenuItem
        {
            ToggleType = MenuItemToggleType.CheckBox,
        };
        _clickThroughItem.Click += (_, _) => _ = ToggleClickThroughFromTrayAsync();

        _languageItem = new NativeMenuItem
        {
            Menu = BuildLanguageMenu(),
        };

        _exitItem = new NativeMenuItem();
        _exitItem.Click += (_, _) => _ = RequestShutdownAsync();

        _trayMenu = new NativeMenu();
        _trayMenu.Items.Add(_openManagementItem);
        _trayMenu.Items.Add(_clickThroughItem);
        _trayMenu.Items.Add(_languageItem);
        _trayMenu.Items.Add(new NativeMenuItemSeparator());
        _trayMenu.Items.Add(_exitItem);

        _tray = new TrayIcon
        {
            Icon = _icon,
            ToolTipText = localization.Get(LocKeys.AppName),
            IsVisible = true,
            Menu = _trayMenu,
        };

        var controlEndpoint = EndpointPaths.ForGlobalDaemon(options.RuntimeRoot);
        _controlServer = GlobalDaemonControlServer.Start(
            controlEndpoint,
            OnControlRequestAsync,
            message => _log($"[control] {message}"));

        _localization.CultureChanged += OnCultureChanged;
        UpdateTray();
    }

    public TrayIcon TrayIcon => _tray;

    public void Start()
    {
        _startupTask = StartTopologyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetimeSource.Cancel();
        _localization.CultureChanged -= OnCultureChanged;

        if (_startupTask is not null)
        {
            try
            {
                await _startupTask.ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _log($"startup task cleanup: {exception.Message}");
            }
        }

        if (_managementWindow is { } window)
        {
            _managementWindow = null;
            window.Close();
        }

        var runtimes = _runtimes.Values.ToList();
        _runtimes.Clear();

        foreach (var runtime in runtimes)
        {
            try
            {
                await runtime.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _log($"character runtime cleanup failed: {exception.Message}");
            }
        }

        try
        {
            await _libraryDaemon.ShutdownAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log($"library daemon cleanup failed: {exception.Message}");
        }

        try
        {
            await _libraryDaemon.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log($"library supervisor cleanup failed: {exception.Message}");
        }

        await _controlServer.DisposeAsync().ConfigureAwait(true);

        _tray.IsVisible = false;
        _tray.Dispose();
        _lifetimeSource.Dispose();
        _log("global daemon stopped");
    }

    public Task<GlobalStatus> GetStatusAsync()
    {
        var libraryInfo = _libraryDaemon.GetProcessInfo();
        var runtimeStatuses = _runtimes.Values
            .Select(runtime => new CharacterRuntimeStatus(
                runtime.CharacterId,
                runtime.CharacterName,
                runtime.GetProcessInfo(),
                runtime.State))
            .ToList();
        var primary = runtimeStatuses.FirstOrDefault();

        if (_startupError is not null)
        {
            libraryInfo = libraryInfo with { Detail = _startupError };
        }

        return Task.FromResult(new GlobalStatus(
            Environment.ProcessId,
            ComponentVersion.Of(typeof(GlobalDaemonService)),
            _options.RuntimeRoot,
            _options.LibraryPath,
            _managementWindow is { IsVisible: true },
            libraryInfo,
            primary?.Process ?? new ProcessInfo(
                ComponentNames.CharacterRuntime,
                Running: false,
                Pid: null,
                Version: null,
                Detail: "not running"),
            primary?.State,
            runtimeStatuses));
    }

    public async Task RequestShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
        {
            return;
        }

        _log("shutdown requested");
        await DisposeAsync().ConfigureAwait(true);
        _lifetime.Shutdown();
    }

    private async Task StartTopologyAsync()
    {
        try
        {
            _log($"topology starting for '{_options.LibraryPath}'");
            await WithTopologyGateAsync(async () =>
            {
                await _libraryDaemon.EnsureStartedAsync(
                    LibraryStartupTimeout,
                    _lifetimeSource.Token).ConfigureAwait(true);

                var characters = await QueryCharactersAsync().ConfigureAwait(true);

                if (characters.Count == 0)
                {
                    await StartPlaceholderWithLeaseAsync().ConfigureAwait(true);
                }
                else
                {
                    _log($"library has {characters.Count} character(s); waiting for an explicit start");
                }
            }).ConfigureAwait(true);

            _startupError = null;
            _log("topology ready");
            UpdateTray();
        }
        catch (Exception exception)
        {
            _startupError = exception.Message;
            _log($"topology start failed: {exception}");
            UpdateTray();
        }
    }

    private async Task<IpcMessage> OnControlRequestAsync(IpcMessage request)
    {
        var operation = Dispatcher.UIThread.InvokeAsync(() => HandleControlRequestAsync(request));
        return await operation.ConfigureAwait(false);
    }

    private async Task<IpcMessage> HandleControlRequestAsync(IpcMessage request)
    {
        try
        {
            switch (request.Method)
            {
                case "status":
                    return Success(ProtocolJson.ToElement(await GetStatusAsync().ConfigureAwait(true)));

                case "open-management":
                    await OpenManagementWindowAsync().ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "ensure-runtime":
                    await EnsureRuntimeAsync().ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "stop-runtime":
                    await StopAllRuntimesAsync().ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "restart-runtime":
                    await RestartAllRuntimesAsync().ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "start-character":
                    if (request.Params is not { } startParameters)
                    {
                        return Failure("start-character requires parameters.");
                    }

                    var startRequest = ProtocolJson.FromElement<CharacterIdRequest>(startParameters);
                    await StartCharacterAsync(startRequest.CharacterId).ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "stop-character":
                    if (request.Params is not { } stopParameters)
                    {
                        return Failure("stop-character requires parameters.");
                    }

                    var stopRequest = ProtocolJson.FromElement<CharacterIdRequest>(stopParameters);
                    await StopCharacterAsync(stopRequest.CharacterId).ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "activate-character":
                    if (request.Params is not { } activateParameters)
                    {
                        return Failure("activate-character requires parameters.");
                    }

                    var activateRequest = ProtocolJson.FromElement<CharacterIdRequest>(activateParameters);
                    await ActivateCharacterAsync(activateRequest.CharacterId).ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "list-characters":
                    return Success(ProtocolJson.ToElement(
                        new List<CharacterSummary>(await QueryCharactersAsync().ConfigureAwait(true))));

                case "restart-library-daemon":
                    await RestartLibraryDaemonAsync().ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "set-click-through":
                    if (request.Params is not { } parameters)
                    {
                        return Failure("set-click-through requires parameters.");
                    }

                    var clickThrough = ProtocolJson.FromElement<SetClickThroughRequest>(parameters);
                    await SetClickThroughAsync(clickThrough.Enabled).ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "shutdown":
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                        await Dispatcher.UIThread.InvokeAsync(RequestShutdownAsync);
                    });
                    return Success(ProtocolJson.ToElement(new CommandResult(true, "shutting down")));

                case "ping":
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                default:
                    return Failure($"Unknown global daemon method '{request.Method}'.");
            }
        }
        catch (Exception exception)
        {
            _log($"control request '{request.Method}' failed: {exception}");
            return Failure(exception.Message);
        }
    }

    private async Task EnsureRuntimeAsync()
    {
        await WithTopologyGateAsync(EnsureRuntimeCoreAsync).ConfigureAwait(true);

        _startupError = null;
        UpdateTray();
    }

    private async Task EnsureRuntimeCoreAsync()
    {
        await _libraryDaemon.EnsureStartedAsync(
            LibraryStartupTimeout,
            _lifetimeSource.Token).ConfigureAwait(true);

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
                await existing.ShowAsync(_lifetimeSource.Token).ConfigureAwait(true);
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
            _lifetimeSource.Token).ConfigureAwait(true);

        // Keep the library daemon alive while Avalonia and the control channel start up in the
        // character runtime; its own library client connects shortly after the control handshake.
        await using var lease = await LibraryClientApi.ConnectAsync(
            _options.LibraryPath,
            ComponentNames.GlobalDaemon,
            timeout: TimeSpan.FromSeconds(5),
            _lifetimeSource.Token).ConfigureAwait(true);

        await runtime.StartAsync(_lifetimeSource.Token).ConfigureAwait(true);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (runtime.State?.LibraryConnected == true)
            {
                return;
            }

            await Task.Delay(100, _lifetimeSource.Token).ConfigureAwait(true);
        }

        _log("warning: a character runtime did not report a library connection during startup");
    }

    private async Task StopPlaceholderAsync()
    {
        if (_runtimes.ContainsKey(PlaceholderRuntimeKey))
        {
            await RemoveRuntimeAsync(PlaceholderRuntimeKey).ConfigureAwait(true);
        }
    }

    private async Task StartCharacterAsync(string characterId)
    {
        await WithTopologyGateAsync(async () =>
        {
            await _libraryDaemon.EnsureStartedAsync(
                LibraryStartupTimeout,
                _lifetimeSource.Token).ConfigureAwait(true);
            await StartCharacterCoreAsync(characterId, characterName: null).ConfigureAwait(true);
        }).ConfigureAwait(true);

        UpdateTray();
    }

    private async Task StopCharacterAsync(string characterId)
    {
        await WithTopologyGateAsync(
            () => RemoveRuntimeAsync(characterId)).ConfigureAwait(true);
        UpdateTray();
    }

    private async Task ActivateCharacterAsync(string characterId)
    {
        if (_runtimes.TryGetValue(characterId, out var runtime))
        {
            await runtime.ShowAsync(_lifetimeSource.Token).ConfigureAwait(true);
        }
    }

    private async Task StopAllRuntimesAsync()
    {
        await WithTopologyGateAsync(async () =>
        {
            foreach (var key in _runtimes.Keys.ToList())
            {
                await RemoveRuntimeAsync(key).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);

        UpdateTray();
    }

    private async Task RestartAllRuntimesAsync()
    {
        await WithTopologyGateAsync(async () =>
        {
            foreach (var key in _runtimes.Keys.ToList())
            {
                await RemoveRuntimeAsync(key).ConfigureAwait(true);
            }

            await EnsureRuntimeCoreAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);

        UpdateTray();
    }

    private CharacterRuntimeSupervisor CreateRuntime(string? characterId, string? characterName)
    {
        var runtime = new CharacterRuntimeSupervisor(
            _options.LibraryPath,
            characterId,
            characterName,
            _options.ComponentsDirectory,
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
            _log($"character runtime cleanup failed: {exception.Message}");
        }
    }

    private async Task RestartLibraryDaemonAsync()
    {
        _log("restarting library daemon");
        await WithTopologyGateAsync(async () =>
        {
            await _libraryDaemon.ShutdownAsync(_lifetimeSource.Token).ConfigureAwait(true);
            await _libraryDaemon.EnsureStartedAsync(
                LibraryStartupTimeout,
                _lifetimeSource.Token).ConfigureAwait(true);
        }).ConfigureAwait(true);

        _log("library daemon restarted");
        UpdateTray();
    }

    private async Task WithTopologyGateAsync(Func<Task> action)
    {
        await _topologyGate.WaitAsync(_lifetimeSource.Token).ConfigureAwait(true);
        try
        {
            await action().ConfigureAwait(true);
        }
        finally
        {
            _topologyGate.Release();
        }
    }

    private async Task SetClickThroughAsync(bool enabled)
    {
        foreach (var runtime in _runtimes.Values.ToList())
        {
            try
            {
                await runtime.SetClickThroughAsync(enabled, _lifetimeSource.Token)
                    .ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _log($"could not change click-through: {exception.Message}");
            }
        }
    }

    private async Task ToggleClickThroughFromTrayAsync()
    {
        var state = _runtimes.Values
            .Select(runtime => runtime.State)
            .FirstOrDefault(current => current is not null);

        if (state is null)
        {
            return;
        }

        try
        {
            await SetClickThroughAsync(!state.ClickThrough).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log($"could not change click-through from the tray: {exception.Message}");
        }
    }

    private async Task<IReadOnlyList<CharacterSummary>> QueryCharactersAsync()
    {
        try
        {
            await using var client = await LibraryClientApi.ConnectAsync(
                _options.LibraryPath,
                ComponentNames.GlobalDaemon,
                timeout: TimeSpan.FromSeconds(5),
                _lifetimeSource.Token).ConfigureAwait(true);

            return await client.ListCharactersAsync(
                timeout: TimeSpan.FromSeconds(5),
                _lifetimeSource.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is IOException
                or System.Net.Sockets.SocketException
                or IpcException
                or TimeoutException
                or OperationCanceledException)
        {
            _log($"could not list characters: {exception.Message}");
            return [];
        }
    }

    private async Task<CharacterRecord?> GetCharacterRecordAsync(string characterId)
    {
        try
        {
            await using var client = await LibraryClientApi.ConnectAsync(
                _options.LibraryPath,
                ComponentNames.GlobalDaemon,
                timeout: TimeSpan.FromSeconds(5),
                _lifetimeSource.Token).ConfigureAwait(true);

            return await client.GetCharacterAsync(
                characterId,
                timeout: TimeSpan.FromSeconds(5),
                _lifetimeSource.Token).ConfigureAwait(true);
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
            _log($"could not read character '{characterId}': {exception.Message}");
            return null;
        }
    }

    private async Task OpenManagementWindowAsync()
    {
        if (_managementWindow is { } existing)
        {
            if (!existing.IsVisible)
            {
                existing.Show();
            }

            existing.Activate();
            return;
        }

        try
        {
            await WithTopologyGateAsync(async () =>
            {
                await _libraryDaemon.EnsureStartedAsync(
                    ManagementStartupTimeout,
                    _lifetimeSource.Token).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log($"could not ensure the library daemon for the management window: {exception.Message}");
        }

        var window = new ManagementWindow(
            _localization,
            _icon,
            _options.LibraryPath,
            GetStatusAsync,
            StartCharacterAsync,
            StopCharacterAsync,
            RestartLibraryDaemonAsync,
            () => SetClickThroughAsync(false),
            message => _log($"[management] {message}"));

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_managementWindow, window))
            {
                _managementWindow = null;
                UpdateTray();
            }
        };

        _managementWindow = window;
        window.Show();
        window.Activate();
        UpdateTray();
    }

    private NativeMenu BuildLanguageMenu()
    {
        var menu = new NativeMenu();

        foreach (var culture in SupportedCultures.All)
        {
            var item = new NativeMenuItem
            {
                ToggleType = MenuItemToggleType.Radio,
            };
            var selectedCulture = culture;
            item.Click += (_, _) => _localization.SetCulture(selectedCulture);
            _languageItems[culture.Name] = item;
            menu.Items.Add(item);
        }

        return menu;
    }

    private void OnRuntimeStateChanged()
    {
        Dispatcher.UIThread.Post(UpdateTray);
    }

    private void OnCultureChanged(CultureInfo culture)
    {
        if (Application.Current is { } application)
        {
            application.Name = _localization.Get(LocKeys.AppName);
        }

        UpdateTray();
        _ = SendLanguageToRuntimeAsync(culture.Name);
    }

    private async Task SendLanguageToRuntimeAsync(string languageTag)
    {
        foreach (var runtime in _runtimes.Values.ToList())
        {
            try
            {
                await runtime.SetLanguageAsync(languageTag, _lifetimeSource.Token)
                    .ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _log($"could not change the character runtime language: {exception.Message}");
            }
        }
    }

    private void UpdateTray()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _openManagementItem.Header = _localization.Get(LocKeys.TrayOpenManagement);
        _clickThroughItem.Header = _localization.Get(LocKeys.TrayClickThrough);
        _clickThroughItem.IsChecked = _runtimes.Values.Any(
            runtime => runtime.State?.ClickThrough == true);
        _clickThroughItem.IsEnabled = _runtimes.Values.Any(
            runtime => runtime.GetProcessInfo().Running);
        _languageItem.Header = _localization.Get(LocKeys.MenuLanguage);
        _exitItem.Header = _localization.Get(LocKeys.TrayExit);
        _tray.ToolTipText = _localization.Get(LocKeys.AppName);

        foreach (var (cultureName, item) in _languageItems)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            item.Header = _localization.Get(GetLanguageKey(culture));
            item.IsChecked = string.Equals(
                cultureName,
                _localization.Culture.Name,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string GetLanguageKey(CultureInfo culture) => culture.Name switch
    {
        SupportedCultures.SimplifiedChinese => LocKeys.LanguageSimplifiedChinese,
        SupportedCultures.English => LocKeys.LanguageEnglish,
        SupportedCultures.Japanese => LocKeys.LanguageJapanese,
        SupportedCultures.Korean => LocKeys.LanguageKorean,
        _ => throw new ArgumentOutOfRangeException(nameof(culture), culture, "Unsupported UI culture."),
    };

    private static IpcMessage Success(System.Text.Json.JsonElement result) => new()
    {
        Ok = true,
        Result = result,
    };

    private static IpcMessage Failure(string error) => new()
    {
        Ok = false,
        Error = error,
    };
}