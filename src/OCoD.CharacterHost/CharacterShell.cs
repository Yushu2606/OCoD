using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using OCoD.Desktop;
using OCoD.Localization;
using OCoD.Protocol;

namespace OCoD.CharacterHost;

/// <summary>
/// Owns the transparent character window, its context menu and the tray entry.
/// This skeleton keeps a single placeholder character; later tickets add character
/// loading, persistence and the daemon topology around the same shell.
/// </summary>
internal sealed class CharacterShell : IDisposable
{
    private const double ResizeBorderWidth = 10;

    private readonly LocalizationService _localization;
    private readonly ClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly WindowIcon _icon;
    private readonly NativeMenu? _trayMenu;
    private readonly NativeMenuItem? _trayClickThroughItem;
    private readonly NativeMenuItem? _trayExitItem;

    private CharacterPlaceholderView _placeholderView;
    private CharacterView? _characterView;
    private CharacterControlClient? _control;
    private LibraryRuntimeConnection? _libraryConnection;
    private DispatcherTimer? _settingsTimer;
    private readonly List<Bitmap> _characterBitmaps = [];
    private string? _characterId;
    private string? _characterName;
    private ResizeSession? _resize;
    private bool _flipped;
    private bool _locked;
    private bool _topmost;
    private bool _clickThrough;
    private bool _windowOpened;
    private bool _positionKnown;
    private bool _applyingSettings;
    private bool _disposed;

    public CharacterShell(
        LocalizationService localization,
        ClassicDesktopStyleApplicationLifetime lifetime,
        bool runSmokeTest = false,
        bool createTray = true)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _icon = DesktopAssets.LoadIcon();

        if (createTray)
        {
            _trayClickThroughItem = new NativeMenuItem
            {
                ToggleType = MenuItemToggleType.CheckBox,
            };
            _trayClickThroughItem.Click += (_, _) => ToggleClickThrough();

            _trayExitItem = new NativeMenuItem();
            _trayExitItem.Click += (_, _) => _lifetime.Shutdown();

            _trayMenu = new NativeMenu();
            _trayMenu.Items.Add(_trayClickThroughItem);
            _trayMenu.Items.Add(new NativeMenuItemSeparator());
            _trayMenu.Items.Add(_trayExitItem);
        }

        _placeholderView = new CharacterPlaceholderView(_localization);
        Window = CreateWindow(_placeholderView);

        if (_trayMenu is not null)
        {
            UpdateTrayMenu();
            TrayIcon = new TrayIcon
            {
                Icon = _icon,
                ToolTipText = _localization.Get(LocKeys.AppName),
                IsVisible = true,
                Menu = _trayMenu,
            };
        }

        _localization.CultureChanged += OnCultureChanged;
        Window.Opened += OnWindowOpened;
        Window.Closed += OnWindowClosed;
        Window.Resized += OnWindowGeometryChanged;
        Window.PositionChanged += OnWindowPositionChanged;

        if (runSmokeTest)
        {
            Window.Opened += OnSmokeTestOpened;
        }
    }

    public Window Window { get; }

    public TrayIcon? TrayIcon { get; }

    public void LoadCharacter(CharacterRecord record, IReadOnlyDictionary<string, Bitmap> images)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(images);

        if (images.Count == 0)
        {
            throw new ArgumentException("A character must have at least one image.", nameof(images));
        }

        _characterId = record.Id;
        _characterName = record.Name;
        _characterBitmaps.AddRange(images.Values.Distinct());
        _characterView = new CharacterView(record, images);
        Window.Content = _characterView;

        _applyingSettings = true;

        try
        {
            _flipped = record.Settings.Flipped;
            _topmost = record.Settings.Topmost;
            _locked = record.Settings.Locked;

            Window.MinWidth = 60;
            Window.MinHeight = 60;
            Window.Width = Math.Clamp(record.Settings.Width, 60, 1200);
            Window.Height = Math.Clamp(record.Settings.Height, 60, 1200);
            Window.Topmost = _topmost;
            Window.Title = record.Name;

            if (record.Settings.X is { } x && record.Settings.Y is { } y)
            {
                _positionKnown = true;
                Window.WindowStartupLocation = WindowStartupLocation.Manual;
                Window.Position = new PixelPoint(x, y);
            }
            else
            {
                _positionKnown = false;
                Window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        finally
        {
            _applyingSettings = false;
        }

        ApplyFlip();
        RefreshMenus();
        PublishState();
    }

    public void AttachControl(CharacterControlClient control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        PublishState();
    }

    public void AttachLibraryConnection(LibraryRuntimeConnection connection)
    {
        _libraryConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        PublishState();
    }

    public void HandleControlCommand(CharacterCommand command)
    {
        switch (command.Name)
        {
            case CharacterCommands.SetClickThrough:
                SetClickThrough(command.Enabled ?? !_clickThrough);
                break;

            case CharacterCommands.SetLanguage:
                if (command.LanguageTag is not null
                    && _localization.SetCulture(command.LanguageTag))
                {
                    PublishState();
                }

                break;

            case CharacterCommands.Show:
                Window.Show();
                Window.Activate();
                break;

            case CharacterCommands.Shutdown:
                _lifetime.Shutdown();
                break;

            default:
                Console.Error.WriteLine($"OCoD: unknown character command '{command.Name}'.");
                break;
        }
    }

    public void PublishState()
    {
        if (_control is null)
        {
            return;
        }

        var state = new CharacterState(
            Visible: Window.IsVisible,
            ClickThrough: _clickThrough,
            Locked: _locked,
            Topmost: _topmost,
            Flipped: _flipped,
            LibraryConnected: _libraryConnection?.IsConnected ?? false,
            Pid: Environment.ProcessId,
            Version: ComponentVersion.Of(typeof(CharacterShell)),
            LibraryPath: _libraryConnection?.LibraryPath,
            CharacterId: _characterId,
            CharacterName: _characterName);

        _ = PublishStateCoreAsync(state);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _localization.CultureChanged -= OnCultureChanged;
        Window.Closed -= OnWindowClosed;

        if (_characterId is not null)
        {
            _settingsTimer?.Stop();
            try
            {
                SaveSettingsNowAsync(force: true).Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }
        }

        _disposed = true;
        Window.ContextMenu?.Close();

        if (TrayIcon is { } tray)
        {
            tray.IsVisible = false;
            tray.Dispose();
        }

        foreach (var bitmap in _characterBitmaps)
        {
            bitmap.Dispose();
        }

        _characterBitmaps.Clear();
        _settingsTimer?.Stop();
    }

    private Window CreateWindow(CharacterPlaceholderView content)
    {
        var window = new Window
        {
            Title = _localization.Get(LocKeys.WindowTitle),
            Icon = _icon,
            Width = 280,
            Height = 380,
            MinWidth = 200,
            MinHeight = 260,
            CanResize = true,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = true,
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            TransparencyBackgroundFallback = Brushes.Transparent,
            Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = content,
            ContextMenu = BuildContextMenu(),
        };

        window.PointerPressed += OnPointerPressed;
        window.PointerMoved += OnPointerMoved;
        window.PointerReleased += OnPointerReleased;
        window.PointerCaptureLost += OnPointerCaptureLost;
        return window;
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(CreateToggleMenuItem(LocKeys.MenuFlip, _flipped, ToggleFlip));
        menu.Items.Add(CreateToggleMenuItem(LocKeys.MenuLock, _locked, ToggleLock));
        menu.Items.Add(CreateToggleMenuItem(LocKeys.MenuTopmost, _topmost, ToggleTopmost));
        menu.Items.Add(CreateToggleMenuItem(LocKeys.MenuClickThrough, _clickThrough, () => ToggleClickThrough()));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildLanguageMenuItem());
        menu.Items.Add(new Separator());

        var exit = new MenuItem
        {
            Header = _localization.Get(LocKeys.MenuExit),
        };
        exit.Click += (_, _) => _lifetime.Shutdown();
        menu.Items.Add(exit);

        return menu;
    }

    private MenuItem CreateToggleMenuItem(string key, bool isChecked, Action toggle)
    {
        var item = new MenuItem
        {
            Header = _localization.Get(key),
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = isChecked,
        };

        item.Click += (_, _) => toggle();
        return item;
    }

    private MenuItem BuildLanguageMenuItem()
    {
        var languageMenu = new MenuItem
        {
            Header = _localization.Get(LocKeys.MenuLanguage),
        };

        foreach (var culture in SupportedCultures.All)
        {
            var item = new MenuItem
            {
                Header = _localization.Get(GetLanguageKey(culture)),
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "language",
                IsChecked = string.Equals(
                    culture.Name,
                    _localization.Culture.Name,
                    StringComparison.Ordinal),
            };

            var selectedCulture = culture;
            item.Click += (_, _) => _localization.SetCulture(selectedCulture);
            languageMenu.Items.Add(item);
        }

        return languageMenu;
    }

    private void UpdateTrayMenu()
    {
        if (_trayClickThroughItem is null || _trayExitItem is null)
        {
            return;
        }

        _trayClickThroughItem.Header = _localization.Get(LocKeys.TrayClickThrough);
        _trayClickThroughItem.IsChecked = _clickThrough;
        _trayExitItem.Header = _localization.Get(LocKeys.TrayExit);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_locked || _clickThrough)
        {
            return;
        }

        var pointer = e.GetCurrentPoint(Window);
        if (!pointer.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (FindResizeEdge(pointer.Position, Window.ClientSize) is { } edge)
        {
            _resize = new ResizeSession(
                edge,
                GetScreenPoint(Window, pointer.Position),
                Window.Position,
                Window.ClientSize.Width,
                Window.ClientSize.Height,
                Window.DesktopScaling);

            e.Pointer.Capture(Window);
        }
        else
        {
            Window.BeginMoveDrag(e);
        }

        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resize is not { } resize)
        {
            return;
        }

        ApplyResize(resize, GetScreenPoint(Window, e.GetPosition(Window)));
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resize is null)
        {
            return;
        }

        EndResize(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _resize = null;

    private void ApplyResize(ResizeSession resize, PixelPoint pointerPosition)
    {
        var scale = resize.Scaling <= 0 ? 1 : resize.Scaling;
        var deltaX = (pointerPosition.X - resize.StartPointer.X) / scale;
        var deltaY = (pointerPosition.Y - resize.StartPointer.Y) / scale;

        var west = resize.Edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
        var east = resize.Edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
        var north = resize.Edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;
        var south = resize.Edge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast;

        var minWidth = Window.MinWidth > 0 ? Window.MinWidth : 80;
        var minHeight = Window.MinHeight > 0 ? Window.MinHeight : 80;

        var width = resize.StartWidth;
        var height = resize.StartHeight;

        if (east)
        {
            width = Math.Max(minWidth, resize.StartWidth + deltaX);
        }
        else if (west)
        {
            width = Math.Max(minWidth, resize.StartWidth - deltaX);
        }

        if (south)
        {
            height = Math.Max(minHeight, resize.StartHeight + deltaY);
        }
        else if (north)
        {
            height = Math.Max(minHeight, resize.StartHeight - deltaY);
        }

        var x = resize.StartWindowPosition.X;
        var y = resize.StartWindowPosition.Y;

        if (west)
        {
            x += (int)Math.Round((resize.StartWidth - width) * scale);
        }

        if (north)
        {
            y += (int)Math.Round((resize.StartHeight - height) * scale);
        }

        Window.Width = width;
        Window.Height = height;
        Window.Position = new PixelPoint(x, y);
    }

    private void EndResize(IPointer pointer)
    {
        _resize = null;
        pointer.Capture(null);
    }

    private static PixelPoint GetScreenPoint(Window window, Point clientPoint)
    {
        var scale = window.DesktopScaling <= 0 ? 1 : window.DesktopScaling;
        var position = window.Position;

        return new PixelPoint(
            position.X + (int)Math.Round(clientPoint.X * scale),
            position.Y + (int)Math.Round(clientPoint.Y * scale));
    }

    private static WindowEdge? FindResizeEdge(Point position, Size clientSize)
    {
        var left = position.X <= ResizeBorderWidth;
        var right = position.X >= clientSize.Width - ResizeBorderWidth;
        var top = position.Y <= ResizeBorderWidth;
        var bottom = position.Y >= clientSize.Height - ResizeBorderWidth;

        if (top && left)
        {
            return WindowEdge.NorthWest;
        }

        if (top && right)
        {
            return WindowEdge.NorthEast;
        }

        if (bottom && left)
        {
            return WindowEdge.SouthWest;
        }

        if (bottom && right)
        {
            return WindowEdge.SouthEast;
        }

        if (left)
        {
            return WindowEdge.West;
        }

        if (right)
        {
            return WindowEdge.East;
        }

        if (top)
        {
            return WindowEdge.North;
        }

        if (bottom)
        {
            return WindowEdge.South;
        }

        return null;
    }

    private void ToggleFlip()
    {
        _flipped = !_flipped;
        ApplyFlip();
        RefreshMenus();
        PublishState();
        ScheduleSettingsSave();
    }

    private void ToggleLock()
    {
        _locked = !_locked;
        RefreshMenus();
        PublishState();
        ScheduleSettingsSave();
    }

    private void ToggleTopmost()
    {
        _topmost = !_topmost;
        Window.Topmost = _topmost;
        RefreshMenus();
        PublishState();
        ScheduleSettingsSave();
    }

    private bool ToggleClickThrough() => SetClickThrough(!_clickThrough);

    private bool SetClickThrough(bool enabled)
    {
        if (_clickThrough == enabled)
        {
            return true;
        }

        if (!ClickThroughService.TrySet(Window, enabled))
        {
            Console.Error.WriteLine("OCoD: click-through is not supported on this platform.");
            return false;
        }

        _clickThrough = enabled;
        if (enabled)
        {
            Window.ContextMenu?.Close();
        }

        RefreshMenus();
        PublishState();
        return true;
    }

    private void ApplyFlip()
    {
        var target = _characterView?.FlipTarget ?? _placeholderView.FlipTarget;

        if (target is null)
        {
            return;
        }

        target.RenderTransformOrigin = RelativePoint.Center;
        target.RenderTransform = _flipped
            ? new ScaleTransform { ScaleX = -1, ScaleY = 1 }
            : null;
    }

    private void RefreshMenus()
    {
        Window.ContextMenu?.Close();
        Window.ContextMenu = BuildContextMenu();
        UpdateTrayMenu();
    }

    private void OnCultureChanged(CultureInfo culture)
    {
        if (_characterView is not null)
        {
            Window.Title = _characterName ?? _localization.Get(LocKeys.WindowTitle);
            Window.Content = _characterView;
        }
        else
        {
            Window.Title = _localization.Get(LocKeys.WindowTitle);
            _placeholderView = new CharacterPlaceholderView(_localization);
            Window.Content = _placeholderView;
        }

        ApplyFlip();

        if (TrayIcon is { } tray)
        {
            tray.ToolTipText = _localization.Get(LocKeys.AppName);
        }

        RefreshMenus();
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        _windowOpened = true;
        PublishState();
    }

    private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

    private void OnWindowGeometryChanged(object? sender, WindowResizedEventArgs e) =>
        ScheduleSettingsSave();

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_windowOpened)
        {
            _positionKnown = true;
        }

        ScheduleSettingsSave();
    }

    private void ScheduleSettingsSave()
    {
        if (_characterId is null || _applyingSettings || !_windowOpened || _disposed)
        {
            return;
        }

        _settingsTimer ??= CreateSettingsTimer();
        _settingsTimer.Stop();
        _settingsTimer.Start();
    }

    private DispatcherTimer CreateSettingsTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };

        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await SaveSettingsNowAsync().ConfigureAwait(true);
        };

        return timer;
    }

    private async Task SaveSettingsNowAsync(bool force = false)
    {
        if (_characterId is null
            || _libraryConnection is null
            || !_libraryConnection.IsConnected
            || (_disposed && !force))
        {
            return;
        }

        var settings = new CharacterRunSettings(
            Window.Width,
            Window.Height,
            _positionKnown ? Window.Position.X : null,
            _positionKnown ? Window.Position.Y : null,
            _flipped,
            _topmost,
            _locked);

        try
        {
            await _libraryConnection.SaveSettingsAsync(_characterId, settings)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException
                or IpcException
                or ObjectDisposedException
                or OperationCanceledException)
        {
            Console.Error.WriteLine($"OCoD: could not save character settings: {exception.Message}");
        }
    }

    private async Task PublishStateCoreAsync(CharacterState state)
    {
        try
        {
            if (_control is not null)
            {
                await _control.PublishStateAsync(state).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or IpcException)
        {
            Console.Error.WriteLine($"OCoD: could not publish character state: {exception.Message}");
        }
    }

    private void OnSmokeTestOpened(object? sender, EventArgs e)
    {
        Window.Opened -= OnSmokeTestOpened;
        Dispatcher.UIThread.Post(RunSmokeTest, DispatcherPriority.Background);
    }

    private void RunSmokeTest()
    {
        var failures = new List<string>();
        var logPath = Path.Combine(Path.GetTempPath(), "ocod-smoke-test.log");
        using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };

        void Write(string line)
        {
            Console.WriteLine(line);
            log.WriteLine(line);
        }

        void Check(bool condition, string description)
        {
            Write($"{(condition ? "ok" : "FAIL")}: {description}");
            if (!condition)
            {
                failures.Add(description);
            }
        }

        try
        {
            Check(Window.IsVisible, "window is visible");
            Check(
                Window.ActualTransparencyLevel.Equals(WindowTransparencyLevel.Transparent),
                $"window is transparent (actual: {Window.ActualTransparencyLevel})");

            Window.ContextMenu?.Open(Window);
            Window.ContextMenu?.Close();
            Check(true, "context menu opens and closes");

            ToggleFlip();
            Check(
                _flipped && _placeholderView.FlipTarget?.RenderTransform is ScaleTransform { ScaleX: -1 },
                "horizontal flip applies a mirror transform");
            ToggleFlip();
            Check(
                !_flipped && _placeholderView.FlipTarget?.RenderTransform is null,
                "horizontal flip can be cleared");

            ToggleLock();
            Check(_locked, "position and size lock can be enabled");
            ToggleLock();
            Check(!_locked, "position and size lock can be disabled");

            ToggleTopmost();
            Check(_topmost && Window.Topmost, "always-on-top can be enabled");
            ToggleTopmost();
            Check(!_topmost && !Window.Topmost, "always-on-top can be disabled");

            Check(
                ToggleClickThrough()
                    && _clickThrough
                    && ClickThroughService.TryGet(Window, out var clickThroughOn)
                    && clickThroughOn,
                "click-through can be enabled");
            Check(
                ToggleClickThrough()
                    && !_clickThrough
                    && ClickThroughService.TryGet(Window, out var clickThroughOff)
                    && !clickThroughOff,
                "click-through can be disabled");

            var originalWidth = Window.Width;
            var originalHeight = Window.Height;
            var originalPosition = Window.Position;
            var scale = Window.DesktopScaling <= 0 ? 1 : Window.DesktopScaling;
            var resizeSession = new ResizeSession(
                WindowEdge.SouthEast,
                new PixelPoint(0, 0),
                originalPosition,
                Window.ClientSize.Width,
                Window.ClientSize.Height,
                scale);

            try
            {
                ApplyResize(
                    resizeSession,
                    new PixelPoint(
                        (int)Math.Round(40 * scale),
                        (int)Math.Round(30 * scale)));

                Check(
                    Math.Abs(Window.Width - (resizeSession.StartWidth + 40)) < 1,
                    "south-east resize updates window width");
                Check(
                    Math.Abs(Window.Height - (resizeSession.StartHeight + 30)) < 1,
                    "south-east resize updates window height");
            }
            finally
            {
                Window.Width = originalWidth;
                Window.Height = originalHeight;
                Window.Position = originalPosition;
            }

            var originalCulture = _localization.Culture;
            foreach (var culture in SupportedCultures.All)
            {
                _localization.SetCulture(culture);
                Check(
                    string.Equals(_localization.Culture.Name, culture.Name, StringComparison.Ordinal),
                    $"UI culture switches to {culture.Name}");
            }

            _localization.SetCulture(originalCulture);
        }
        catch (Exception exception)
        {
            failures.Add(exception.ToString());
            Write(exception.ToString());
        }

        if (failures.Count == 0)
        {
            Write($"OCoD smoke test passed. Log: {logPath}");
            Environment.ExitCode = 0;
            _lifetime.Shutdown();
            return;
        }

        Write($"OCoD smoke test failed: {string.Join("; ", failures)}");
        Write($"Log: {logPath}");
        Environment.ExitCode = 1;
        _lifetime.Shutdown(1);
    }

    private static string GetLanguageKey(CultureInfo culture) => culture.Name switch
    {
        SupportedCultures.SimplifiedChinese => LocKeys.LanguageSimplifiedChinese,
        SupportedCultures.English => LocKeys.LanguageEnglish,
        SupportedCultures.Japanese => LocKeys.LanguageJapanese,
        SupportedCultures.Korean => LocKeys.LanguageKorean,
        _ => throw new ArgumentOutOfRangeException(nameof(culture), culture, "Unsupported UI culture."),
    };

    private sealed record ResizeSession(
        WindowEdge Edge,
        PixelPoint StartPointer,
        PixelPoint StartWindowPosition,
        double StartWidth,
        double StartHeight,
        double Scaling);
}