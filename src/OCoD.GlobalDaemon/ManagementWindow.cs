using System.Globalization;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using OCoD.Localization;
using OCoD.Protocol;

using LibraryClientApi = OCoD.LibraryClient.LibraryClient;

namespace OCoD.GlobalDaemon;

/// <summary>A normal window for importing characters and observing/driving the runtime topology.</summary>
internal sealed class ManagementWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly string _libraryPath;
    private readonly string _libraryDisplayName;
    private readonly Func<Task<GlobalStatus>> _getStatus;
    private readonly Func<string, Task> _startCharacter;
    private readonly Func<string, Task> _stopCharacter;
    private readonly Func<Task> _restartLibrary;
    private readonly Func<Task> _restoreInteraction;
    private readonly Action<string> _log;
    private readonly ManagementView _view;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetime = new();

    private LibraryClientApi? _libraryClient;
    private GlobalStatus? _lastGlobalStatus;
    private LibraryStatus? _lastLibraryStatus;
    private IReadOnlyList<CharacterSummary> _lastCharacters = [];
    private bool _refreshing;
    private bool _closed;

    public ManagementWindow(
        LocalizationService localization,
        WindowIcon icon,
        string libraryPath,
        string libraryDisplayName,
        Func<Task<GlobalStatus>> getStatus,
        Func<string, Task> startCharacter,
        Func<string, Task> stopCharacter,
        Func<Task> restartLibrary,
        Func<Task> restoreInteraction,
        Action<string> log)
    {
        _localization = localization;
        _libraryPath = libraryPath;
        _libraryDisplayName = libraryDisplayName;
        _getStatus = getStatus;
        _startCharacter = startCharacter;
        _stopCharacter = stopCharacter;
        _restartLibrary = restartLibrary;
        _restoreInteraction = restoreInteraction;
        _log = log;

        Title = BuildTitle();
        Icon = icon;
        Width = 720;
        Height = 720;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        _view = new ManagementView(
            localization,
            () => RunActionAsync(() => Task.CompletedTask),
            () => RunActionAsync(_restartLibrary),
            () => RunActionAsync(_restoreInteraction),
            id => RunActionAsync(() => _startCharacter(id)),
            id => RunActionAsync(() => _stopCharacter(id)),
            ImportAsync,
            CloseAsync);
        Content = _view;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _timer.Tick += (_, _) => _ = RefreshAsync();

        Opened += OnWindowOpened;
        Closed += OnWindowClosed;
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var importGesture = e.Key == Key.I
            && (e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                || e.KeyModifiers.HasFlag(KeyModifiers.Control));

        if (!importGesture)
        {
            return;
        }

        e.Handled = true;
        _ = ImportAsync();
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        _localization.CultureChanged += OnCultureChanged;
        _timer.Start();
        _ = RefreshAsync();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _timer.Stop();
        _localization.CultureChanged -= OnCultureChanged;
        _lifetime.Cancel();

        var libraryClient = _libraryClient;
        _libraryClient = null;
        if (libraryClient is not null)
        {
            _ = libraryClient.DisposeAsync().AsTask();
        }

        _lifetime.Dispose();
    }

    private void OnCultureChanged(CultureInfo culture)
    {
        Title = BuildTitle();

        if (_lastGlobalStatus is not null)
        {
            _view.Apply(_lastGlobalStatus, _lastLibraryStatus, _lastCharacters);
        }
    }

    private string BuildTitle() =>
        $"{_localization.Get(LocKeys.ManagementTitle)} — {_libraryDisplayName}";

    private async Task RefreshAsync()
    {
        if (_refreshing || _closed)
        {
            return;
        }

        _refreshing = true;

        try
        {
            await EnsureLibraryClientAsync().ConfigureAwait(true);

            var global = await _getStatus().ConfigureAwait(true);
            LibraryStatus? library = null;
            IReadOnlyList<CharacterSummary> characters = [];

            if (_libraryClient is { } client)
            {
                try
                {
                    library = await client.GetStatusAsync(
                        timeout: TimeSpan.FromSeconds(2),
                        _lifetime.Token).ConfigureAwait(true);
                    characters = await client.ListCharactersAsync(
                        timeout: TimeSpan.FromSeconds(2),
                        _lifetime.Token).ConfigureAwait(true);
                }
                catch (Exception exception) when (
                    exception is IOException
                        or System.Net.Sockets.SocketException
                        or IpcException
                        or TimeoutException
                        or OperationCanceledException)
                {
                    _log($"management window lost the library daemon: {exception.Message}");
                    await DisposeLibraryClientAsync().ConfigureAwait(true);
                }
            }

            _lastGlobalStatus = global;
            _lastLibraryStatus = library;
            _lastCharacters = characters;
            _view.Apply(global, library, characters);
        }
        catch (Exception exception)
        {
            _view.ShowError(exception.Message);
            _log($"management refresh failed: {exception}");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task ImportAsync()
    {
        if (_closed)
        {
            return;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _localization.Get(LocKeys.ManagementSelectPackage),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("OCoD character package")
                    {
                        Patterns = ["*.cvclk"],
                    },
                ],
            }).ConfigureAwait(true);

            if (files.Count == 0)
            {
                return;
            }

            var localPath = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(localPath))
            {
                _view.ShowImportMessage(
                    _localization.Get(
                        LocKeys.ManagementImportFailed,
                        _localization.Get(LocKeys.ManagementNoLocalPath)),
                    isError: true);
                return;
            }

            await EnsureLibraryClientAsync().ConfigureAwait(true);
            if (_libraryClient is not { } client)
            {
                _view.ShowImportMessage(
                    _localization.Get(
                        LocKeys.ManagementImportFailed,
                        _localization.Get(LocKeys.ManagementLibraryUnavailable)),
                    isError: true);
                return;
            }

            var result = await client.ImportCvclkAsync(localPath).ConfigureAwait(true);
            _view.ShowImportMessage(
                _localization.Get(
                    LocKeys.ManagementImportSummary,
                    result.Imported.Count,
                    result.Skipped.Count),
                isError: result.Imported.Count == 0);

            foreach (var imported in result.Imported)
            {
                try
                {
                    await _startCharacter(imported.Id).ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    _view.ShowError(exception.Message);
                    _log($"could not start imported character {imported.Id}: {exception}");
                }
            }

            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _view.ShowImportMessage(
                _localization.Get(LocKeys.ManagementImportFailed, exception.Message),
                isError: true);
            _log($"import failed: {exception}");
        }
    }

    private async Task EnsureLibraryClientAsync()
    {
        if (_libraryClient is not null || _closed)
        {
            return;
        }

        try
        {
            _libraryClient = await LibraryClientApi.ConnectAsync(
                _libraryPath,
                ComponentNames.ManagementWindow,
                timeout: TimeSpan.FromSeconds(1),
                _lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is IOException
                or System.Net.Sockets.SocketException
                or IpcException
                or TimeoutException
                or OperationCanceledException)
        {
            // The daemon may be starting or exiting; retry on the next tick.
        }
    }

    private async Task DisposeLibraryClientAsync()
    {
        var client = _libraryClient;
        _libraryClient = null;

        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(true);
        }
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        if (_closed)
        {
            return;
        }

        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _view.ShowError(exception.Message);
            _log($"management action failed: {exception}");
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private Task CloseAsync()
    {
        Close();
        return Task.CompletedTask;
    }
}