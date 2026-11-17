using System.Globalization;
using System.Text.Json;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using OCoD.Desktop;
using OCoD.Localization;
using OCoD.Protocol;

namespace OCoD.GlobalDaemon;

/// <summary>
/// Owns the tray entry, the per-library runtime hosts and the global control channel. One global
/// daemon can serve several distribution directories; each library gets its own library daemon,
/// character runtimes and management window.
/// </summary>
internal sealed class GlobalDaemonService : IAsyncDisposable
{
    private static readonly StringComparer LibraryPathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly GlobalDaemonOptions _options;
    private readonly LocalizationService _localization;
    private readonly ClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly Action<string> _log;
    private readonly WindowIcon _icon;
    private readonly Dictionary<string, LibraryRuntimeHost> _libraries =
        new(LibraryPathComparer);
    private readonly LibraryRuntimeHost _primaryLibrary;
    private readonly GlobalDaemonControlServer _controlServer;
    private readonly NativeMenu _trayMenu;
    private readonly NativeMenu _librariesMenu;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _openManagementItem;
    private readonly NativeMenuItem _librariesItem;
    private readonly NativeMenuItem _clickThroughItem;
    private readonly NativeMenuItem _languageItem;
    private readonly NativeMenuItem _exitItem;
    private readonly Dictionary<string, NativeMenuItem> _languageItems =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NativeMenuItem> _libraryItems =
        new(LibraryPathComparer);
    private readonly CancellationTokenSource _lifetimeSource = new();

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

        _librariesMenu = new NativeMenu();
        _primaryLibrary = RegisterLibraryHost(options.LibraryPath);

        _openManagementItem = new NativeMenuItem();
        _openManagementItem.Click += (_, _) => _ = OpenManagementWindowAsync(_primaryLibrary);

        _librariesItem = new NativeMenuItem
        {
            Menu = _librariesMenu,
        };

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
        _trayMenu.Items.Add(_librariesItem);
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

        var libraries = _libraries.Values.ToList();
        _libraries.Clear();

        foreach (var library in libraries)
        {
            library.Changed -= OnLibraryChanged;

            try
            {
                await library.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _log($"library host cleanup failed: {exception.Message}");
            }
        }

        _libraryItems.Clear();
        await _controlServer.DisposeAsync().ConfigureAwait(true);

        _tray.IsVisible = false;
        _tray.Dispose();
        _lifetimeSource.Dispose();
        _log("global daemon stopped");
    }

    public Task<GlobalStatus> GetStatusAsync()
    {
        var primary = _primaryLibrary.GetStatus();

        if (_startupError is not null)
        {
            primary = primary with
            {
                LibraryDaemon = primary.LibraryDaemon with { Detail = _startupError },
            };
        }

        return Task.FromResult(primary with
        {
            Libraries = _libraries.Values.Select(library => library.GetLibraryStatus()).ToList(),
        });
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

    private LibraryRuntimeHost RegisterLibraryHost(string libraryPath)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new InvalidOperationException("The global daemon is shutting down.");
        }

        var normalized = LibraryRuntimeHost.NormalizeLibraryPath(libraryPath);
        if (_libraries.TryGetValue(normalized, out var existing))
        {
            return existing;
        }

        var host = new LibraryRuntimeHost(
            normalized,
            BuildLibraryDisplayName(normalized),
            _options.RuntimeRoot,
            _options.ComponentsDirectory,
            _localization,
            _icon,
            _lifetimeSource.Token,
            _log);
        host.Changed += OnLibraryChanged;
        _libraries.Add(normalized, host);

        var item = new NativeMenuItem
        {
            Header = host.DisplayName,
        };
        item.Click += (_, _) => _ = OpenManagementWindowAsync(host);
        _librariesMenu.Items.Add(item);
        _libraryItems[normalized] = item;
        _log($"registered library '{normalized}'");
        return host;
    }

    private string BuildLibraryDisplayName(string libraryPath)
    {
        var baseName = LibraryRuntimeHost.GetDefaultDisplayName(libraryPath);
        var candidate = baseName;
        var suffix = 2;

        while (_libraries.Values.Any(library => string.Equals(
            library.DisplayName,
            candidate,
            StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} ({suffix++})";
        }

        return candidate;
    }

    private LibraryRuntimeHost ResolveLibraryHost(string? libraryPath) =>
        string.IsNullOrWhiteSpace(libraryPath)
            ? _primaryLibrary
            : RegisterLibraryHost(libraryPath);

    private LibraryRuntimeHost ResolveRequestLibrary(JsonElement? parameters)
    {
        if (parameters is not { } element)
        {
            return _primaryLibrary;
        }

        var request = ProtocolJson.FromElement<LibraryPathRequest>(element);
        return ResolveLibraryHost(request.LibraryPath);
    }

    private async Task StartTopologyAsync()
    {
        try
        {
            await _primaryLibrary.StartAsync(startPlaceholderWhenEmpty: true).ConfigureAwait(true);
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
                    if (request.Params is { } statusParameters)
                    {
                        var statusRequest = ProtocolJson.FromElement<LibraryPathRequest>(
                            statusParameters);
                        if (!string.IsNullOrWhiteSpace(statusRequest.LibraryPath))
                        {
                            return Success(ProtocolJson.ToElement(
                                ResolveLibraryHost(statusRequest.LibraryPath).GetStatus()));
                        }
                    }

                    return Success(ProtocolJson.ToElement(await GetStatusAsync().ConfigureAwait(true)));

                case "open-management":
                    await OpenManagementWindowAsync(ResolveRequestLibrary(request.Params))
                        .ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "ensure-library":
                    await ResolveRequestLibrary(request.Params).EnsureLibraryDaemonAsync()
                        .ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "ensure-runtime":
                    await ResolveRequestLibrary(request.Params).EnsureRuntimeAsync()
                        .ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "stop-runtime":
                    await ResolveRequestLibrary(request.Params).StopAllRuntimesAsync()
                        .ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "restart-runtime":
                    await ResolveRequestLibrary(request.Params).RestartAllRuntimesAsync()
                        .ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "start-character":
                    if (request.Params is not { } startParameters)
                    {
                        return Failure("start-character requires parameters.");
                    }

                    var startRequest = ProtocolJson.FromElement<CharacterIdRequest>(startParameters);
                    await ResolveLibraryHost(startRequest.LibraryPath)
                        .StartCharacterAsync(startRequest.CharacterId)
                        .ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "stop-character":
                    if (request.Params is not { } stopParameters)
                    {
                        return Failure("stop-character requires parameters.");
                    }

                    var stopRequest = ProtocolJson.FromElement<CharacterIdRequest>(stopParameters);
                    await ResolveLibraryHost(stopRequest.LibraryPath)
                        .StopCharacterAsync(stopRequest.CharacterId)
                        .ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "activate-character":
                    if (request.Params is not { } activateParameters)
                    {
                        return Failure("activate-character requires parameters.");
                    }

                    var activateRequest = ProtocolJson.FromElement<CharacterIdRequest>(
                        activateParameters);
                    await ResolveLibraryHost(activateRequest.LibraryPath)
                        .ActivateCharacterAsync(activateRequest.CharacterId)
                        .ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "list-characters":
                    var characters = await ResolveRequestLibrary(request.Params)
                        .ListCharactersAsync()
                        .ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(
                        new List<CharacterSummary>(characters)));

                case "restart-library-daemon":
                    await ResolveRequestLibrary(request.Params).RestartLibraryDaemonAsync()
                        .ConfigureAwait(true);
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "close-management":
                    ResolveRequestLibrary(request.Params).CloseManagementWindow();
                    return Success(ProtocolJson.ToElement(new CommandResult(true)));

                case "set-click-through":
                    if (request.Params is not { } parameters)
                    {
                        return Failure("set-click-through requires parameters.");
                    }

                    var clickThrough = ProtocolJson.FromElement<SetClickThroughRequest>(parameters);
                    await SetClickThroughAsync(
                        clickThrough.Enabled,
                        string.IsNullOrWhiteSpace(clickThrough.LibraryPath)
                            ? null
                            : ResolveLibraryHost(clickThrough.LibraryPath)).ConfigureAwait(true);
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

    private async Task OpenManagementWindowAsync(LibraryRuntimeHost library)
    {
        try
        {
            await library.OpenManagementWindowAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log($"could not open the management window: {exception}");
        }

        UpdateTray();
    }

    private async Task SetClickThroughAsync(bool enabled, LibraryRuntimeHost? library = null)
    {
        var libraries = library is null
            ? _libraries.Values.ToList()
            : [library];

        foreach (var target in libraries)
        {
            await target.SetClickThroughAsync(enabled).ConfigureAwait(true);
        }
    }

    private async Task ToggleClickThroughFromTrayAsync()
    {
        var state = _libraries.Values
            .SelectMany(library => library.CharacterRuntimes)
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

    private void OnLibraryChanged()
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
        foreach (var library in _libraries.Values.ToList())
        {
            await library.SetLanguageAsync(languageTag).ConfigureAwait(true);
        }
    }

    private void UpdateTray()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var runtimes = _libraries.Values
            .SelectMany(library => library.CharacterRuntimes)
            .ToList();

        _openManagementItem.Header = _localization.Get(LocKeys.TrayOpenManagement);
        _openManagementItem.IsEnabled = _libraries.Count > 0;
        _librariesItem.Header = _localization.Get(LocKeys.TrayLibraries);
        _librariesItem.IsVisible = _libraries.Count > 1;
        _clickThroughItem.Header = _localization.Get(LocKeys.TrayClickThrough);
        _clickThroughItem.IsChecked = runtimes.Any(runtime => runtime.State?.ClickThrough == true);
        _clickThroughItem.IsEnabled = runtimes.Any(runtime => runtime.Process.Running);
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

    private static IpcMessage Success(JsonElement result) => new()
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