using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Declarative;
using Avalonia.Media;

using OCoD.Localization;
using OCoD.Protocol;

namespace OCoD.GlobalDaemon;

/// <summary>Library characters, topology and development controls for one library.</summary>
internal sealed class ManagementView : ViewBase
{
    private readonly LocalizationService _localization;
    private readonly Func<Task> _refresh;
    private readonly Func<Task> _restartLibrary;
    private readonly Func<Task> _restoreInteraction;
    private readonly Func<string, Task> _startCharacter;
    private readonly Func<string, Task> _stopCharacter;
    private readonly Func<Task> _import;
    private readonly Func<Task> _close;

    private TextBlock _title = null!;
    private TextBlock _libraryPath = null!;
    private TextBlock _globalLabel = null!;
    private TextBlock _globalValue = null!;
    private TextBlock _libraryLabel = null!;
    private TextBlock _libraryValue = null!;
    private TextBlock _clientsHeader = null!;
    private TextBlock _clientsValue = null!;
    private TextBlock _charactersHeader = null!;
    private StackPanel _charactersPanel = null!;
    private TextBlock _noCharacters = null!;
    private TextBlock _importMessage = null!;
    private TextBlock _error = null!;
    private Button _refreshButton = null!;
    private Button _importButton = null!;
    private Button _restartLibraryButton = null!;
    private Button _restoreButton = null!;
    private Button _closeButton = null!;
    private string? _characterSignature;

    public ManagementView(
        LocalizationService localization,
        Func<Task> refresh,
        Func<Task> restartLibrary,
        Func<Task> restoreInteraction,
        Func<string, Task> startCharacter,
        Func<string, Task> stopCharacter,
        Func<Task> import,
        Func<Task> close)
        : base(ViewInitializationStrategy.Lazy)
    {
        _localization = localization;
        _refresh = refresh;
        _restartLibrary = restartLibrary;
        _restoreInteraction = restoreInteraction;
        _startCharacter = startCharacter;
        _stopCharacter = stopCharacter;
        _import = import;
        _close = close;
        Initialize();
    }

    public void Apply(
        GlobalStatus global,
        LibraryStatus? library,
        IReadOnlyList<CharacterSummary> characters)
    {
        _title.Text = _localization.Get(LocKeys.ManagementTitle);
        _libraryPath.Text = _localization.Get(LocKeys.ManagementLibraryPath, global.LibraryPath);

        _globalLabel.Text = _localization.Get(LocKeys.ManagementGlobalDaemon);
        _globalValue.Text = _localization.Get(
            LocKeys.ManagementGlobalDetail,
            global.Pid,
            global.Version);

        _libraryLabel.Text = _localization.Get(LocKeys.ManagementLibraryDaemon);
        _libraryValue.Text = library is null
            ? _localization.Get(LocKeys.ManagementStopped)
            : _localization.Get(
                LocKeys.ManagementLibraryDetail,
                library.DaemonPid,
                library.DaemonVersion,
                library.Clients.Count);

        _clientsHeader.Text = _localization.Get(LocKeys.ManagementClients);
        _clientsValue.Text = BuildClientList(library);

        _charactersHeader.Text = _localization.Get(LocKeys.ManagementCharacters);
        _noCharacters.Text = _localization.Get(LocKeys.ManagementNoCharacters);
        _noCharacters.IsVisible = characters.Count == 0;

        var runningIds = global.CharacterRuntimes?
            .Where(runtime => runtime.CharacterId is not null && runtime.Process.Running)
            .Select(runtime => runtime.CharacterId!)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        var signature = _localization.Culture.Name + "\n" + string.Join(
            "\n",
            characters.Select(character =>
                $"{character.Id}|{character.Name}|{runningIds.Contains(character.Id)}"));

        if (!string.Equals(signature, _characterSignature, StringComparison.Ordinal))
        {
            _characterSignature = signature;
            RebuildCharacters(characters, runningIds);
        }

        _refreshButton.Content = _localization.Get(LocKeys.ManagementRefresh);
        _importButton.Content = _localization.Get(LocKeys.ManagementImport);
        _restartLibraryButton.Content = _localization.Get(LocKeys.ManagementRestartLibrary);
        _restoreButton.Content = _localization.Get(LocKeys.ManagementRestoreInteraction);
        _closeButton.Content = _localization.Get(LocKeys.ManagementClose);
        _restoreButton.IsEnabled = global.CharacterRuntimes?.Any(
            runtime => runtime.State?.ClickThrough == true) == true;

        _error.IsVisible = false;
    }

    public void ShowImportMessage(string? message, bool isError)
    {
        _importMessage.Text = message ?? string.Empty;
        _importMessage.Foreground = isError ? Brushes.IndianRed : Brushes.SeaGreen;
        _importMessage.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    public void ShowError(string? message)
    {
        _error.Text = message ?? string.Empty;
        _error.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    protected override object Build()
    {
        _title = new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
        };

        _libraryPath = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0x60, 0x70, 0x80)),
            TextWrapping = TextWrapping.Wrap,
        };

        _globalLabel = CreateLabel();
        _globalValue = CreateValue();
        _libraryLabel = CreateLabel();
        _libraryValue = CreateValue();

        var statusCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x12, 0x20, 0x60, 0x90)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0x20, 0x60, 0x90)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    _globalLabel,
                    _globalValue,
                    _libraryLabel,
                    _libraryValue,
                },
            },
        };

        _clientsHeader = CreateLabel();
        _clientsValue = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

        _charactersHeader = CreateLabel();
        _charactersPanel = new StackPanel
        {
            Spacing = 6,
        };
        _noCharacters = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0x60, 0x70, 0x80)),
            TextWrapping = TextWrapping.Wrap,
        };

        _importButton = CreateButton(_import);
        _importMessage = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        _error = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        _refreshButton = CreateButton(_refresh);
        _restartLibraryButton = CreateButton(_restartLibrary);
        _restoreButton = CreateButton(_restoreInteraction);
        _closeButton = CreateButton(_close);

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _refreshButton,
                _restartLibraryButton,
                _restoreButton,
                _closeButton,
            },
        };

        var content = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(22),
            Children =
            {
                _title,
                _libraryPath,
                statusCard,
                _charactersHeader,
                _charactersPanel,
                _noCharacters,
                _importButton,
                _importMessage,
                _clientsHeader,
                _clientsValue,
                _error,
                buttons,
            },
        };

        return new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
    }

    private void RebuildCharacters(
        IReadOnlyList<CharacterSummary> characters,
        HashSet<string> runningIds)
    {
        _charactersPanel.Children.Clear();

        foreach (var character in characters)
        {
            var running = runningIds.Contains(character.Id);
            var name = new TextBlock
            {
                Text = character.Name,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var status = new TextBlock
            {
                Text = _localization.Get(
                    running ? LocKeys.ManagementRunning : LocKeys.ManagementStopped),
                FontSize = 12,
                Foreground = new SolidColorBrush(
                    running
                        ? Color.FromArgb(0xFF, 0x1B, 0x7A, 0x43)
                        : Color.FromArgb(0xFF, 0x70, 0x70, 0x70)),
                Margin = new Thickness(12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var toggleButton = new Button
            {
                Content = _localization.Get(
                    running ? LocKeys.ManagementStop : LocKeys.ManagementStart),
                Padding = new Thickness(12, 5),
            };

            toggleButton.Click += async (_, _) =>
            {
                toggleButton.IsEnabled = false;
                try
                {
                    if (running)
                    {
                        await _stopCharacter(character.Id);
                    }
                    else
                    {
                        await _startCharacter(character.Id);
                    }
                }
                finally
                {
                    toggleButton.IsEnabled = true;
                }
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                Children =
                {
                    name,
                    status,
                    toggleButton,
                },
            };
            Grid.SetColumn(status, 1);
            Grid.SetColumn(toggleButton, 2);

            _charactersPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x0C, 0x20, 0x60, 0x90)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8),
                Child = grid,
            });
        }
    }

    private string BuildClientList(LibraryStatus? library)
    {
        if (library is null || library.Clients.Count == 0)
        {
            return _localization.Get(LocKeys.ManagementNoClients);
        }

        return string.Join(
            Environment.NewLine,
            library.Clients.Select(client =>
                _localization.Get(
                    LocKeys.ManagementClientLine,
                    GetClientLabel(client.ClientName),
                    client.Pid)));
    }

    private string GetClientLabel(string clientName) => clientName switch
    {
        ComponentNames.CharacterRuntime => _localization.Get(LocKeys.ManagementCharacterRuntime),
        ComponentNames.ManagementWindow => _localization.Get(LocKeys.ManagementClientManagement),
        ComponentNames.GlobalDaemon => _localization.Get(LocKeys.ManagementGlobalDaemon),
        ComponentNames.LibraryDaemon => _localization.Get(LocKeys.ManagementLibraryDaemon),
        _ => _localization.Get(LocKeys.ManagementClientUnknown),
    };

    private Button CreateButton(Func<Task> action)
    {
        var button = new Button
        {
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(14, 7),
        };

        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await action();
            }
            finally
            {
                button.IsEnabled = true;
            }
        };

        return button;
    }

    private TextBlock CreateLabel() => new()
    {
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0x40, 0x50, 0x60)),
    };

    private TextBlock CreateValue() => new()
    {
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 2),
    };
}