namespace OCoD.Localization;

/// <summary>
/// Every user-visible string in the application is addressed by one of these keys.
/// The matching copy lives in <c>Resources/strings.&lt;culture&gt;.json</c>.
/// </summary>
public static class LocKeys
{
    public const string AppName = "App.Name";

    public const string WindowTitle = "Shell.Window.Title";
    public const string PlaceholderTitle = "Shell.Placeholder.Title";
    public const string PlaceholderHint = "Shell.Placeholder.Hint";

    public const string MenuFlip = "Menu.Flip";
    public const string MenuLock = "Menu.Lock";
    public const string MenuTopmost = "Menu.Topmost";
    public const string MenuClickThrough = "Menu.ClickThrough";
    public const string MenuLanguage = "Menu.Language";
    public const string MenuExit = "Menu.Exit";

    public const string TrayClickThrough = "Tray.ClickThrough";
    public const string TrayExit = "Tray.Exit";
    public const string TrayOpenManagement = "Tray.OpenManagement";
    public const string TrayLibraries = "Tray.Libraries";

    public const string ManagementTitle = "Management.Title";
    public const string ManagementLibraryPath = "Management.LibraryPath";
    public const string ManagementGlobalDaemon = "Management.GlobalDaemon";
    public const string ManagementLibraryDaemon = "Management.LibraryDaemon";
    public const string ManagementCharacterRuntime = "Management.CharacterRuntime";
    public const string ManagementGlobalDetail = "Management.GlobalDetail";
    public const string ManagementLibraryDetail = "Management.LibraryDetail";
    public const string ManagementCharacterDetail = "Management.CharacterDetail";
    public const string ManagementRunning = "Management.Running";
    public const string ManagementStopped = "Management.Stopped";
    public const string ManagementSwitchOn = "Management.SwitchOn";
    public const string ManagementSwitchOff = "Management.SwitchOff";
    public const string ManagementCharacters = "Management.Characters";
    public const string ManagementNoCharacters = "Management.NoCharacters";
    public const string ManagementImport = "Management.Import";
    public const string ManagementSelectPackage = "Management.SelectPackage";
    public const string ManagementImportSummary = "Management.ImportSummary";
    public const string ManagementImportFailed = "Management.ImportFailed";
    public const string ManagementNoLocalPath = "Management.NoLocalPath";
    public const string ManagementLibraryUnavailable = "Management.LibraryUnavailable";
    public const string ManagementStart = "Management.Start";
    public const string ManagementStop = "Management.Stop";
    public const string ManagementClients = "Management.Clients";
    public const string ManagementNoClients = "Management.NoClients";
    public const string ManagementClientLine = "Management.ClientLine";
    public const string ManagementClientManagement = "Management.ClientManagement";
    public const string ManagementClientUnknown = "Management.ClientUnknown";
    public const string ManagementEmptyLibrary = "Management.EmptyLibrary";
    public const string ManagementRefresh = "Management.Refresh";
    public const string ManagementStartCharacter = "Management.StartCharacter";
    public const string ManagementStopCharacter = "Management.StopCharacter";
    public const string ManagementRestartCharacter = "Management.RestartCharacter";
    public const string ManagementRestartLibrary = "Management.RestartLibrary";
    public const string ManagementRestoreInteraction = "Management.RestoreInteraction";
    public const string ManagementClose = "Management.Close";
    public const string ManagementPackageFileType = "Management.PackageFileType";

    public const string WarningUnrecognizedLanguage = "Warning.UnrecognizedLanguage";

    public const string DaemonAlreadyRunning = "Daemon.AlreadyRunning";
    public const string DaemonOpenManagementFailed = "Daemon.OpenManagementFailed";
    public const string DaemonControlUnreachable = "Daemon.ControlUnreachable";

    public const string CharacterErrorControlToken = "Character.Error.ControlToken";
    public const string CharacterErrorConnectDaemon = "Character.Error.ConnectDaemon";
    public const string CharacterErrorCharacterNeedsLibrary = "Character.Error.CharacterNeedsLibrary";
    public const string CharacterErrorLoadCharacter = "Character.Error.LoadCharacter";
    public const string CharacterErrorNoDisplayableImage = "Character.Error.NoDisplayableImage";
    public const string CharacterErrorNoImages = "Character.Error.NoImages";
    public const string CharacterErrorUnknownCommand = "Character.Error.UnknownCommand";
    public const string CharacterErrorClickThroughUnsupported = "Character.Error.ClickThroughUnsupported";
    public const string CharacterErrorSaveSettings = "Character.Error.SaveSettings";
    public const string CharacterErrorPublishState = "Character.Error.PublishState";

    public const string LauncherUsage = "Launcher.Usage";
    public const string LauncherErrorWidthNumber = "Launcher.Error.WidthNumber";
    public const string LauncherErrorHeightNumber = "Launcher.Error.HeightNumber";
    public const string LauncherErrorFlipOnOff = "Launcher.Error.FlipOnOff";
    public const string LauncherErrorTopmostOnOff = "Launcher.Error.TopmostOnOff";
    public const string LauncherErrorUnknownArgument = "Launcher.Error.UnknownArgument";
    public const string LauncherErrorImportPathRequired = "Launcher.Error.ImportPathRequired";
    public const string LauncherErrorCharacterIdRequired = "Launcher.Error.CharacterIdRequired";
    public const string LauncherErrorCommandFailed = "Launcher.Error.CommandFailed";
    public const string LauncherErrorStartCharacter = "Launcher.Error.StartCharacter";
    public const string LauncherErrorUnsupportedCommand = "Launcher.Error.UnsupportedCommand";
    public const string LauncherErrorStartDaemon = "Launcher.Error.StartDaemon";
    public const string LauncherErrorDaemonNotReady = "Launcher.Error.DaemonNotReady";
    public const string LauncherErrorNoStatus = "Launcher.Error.NoStatus";
    public const string LauncherErrorImportPath = "Launcher.Error.ImportPath";
    public const string LauncherErrorCharacterId = "Launcher.Error.CharacterId";
    public const string LauncherErrorLibraryDaemon = "Launcher.Error.LibraryDaemon";
    public const string LauncherErrorLibraryDaemonTimeout = "Launcher.Error.LibraryDaemonTimeout";
    public const string LauncherErrorShutdown = "Launcher.Error.Shutdown";
    public const string LauncherErrorStopTimeout = "Launcher.Error.StopTimeout";
    public const string LauncherErrorGeneral = "Launcher.Error.General";
    public const string LauncherGlobalDaemonNotRunning = "Launcher.GlobalDaemonNotRunning";
    public const string LauncherStopped = "Launcher.Stopped";
    public const string LauncherStartedDaemon = "Launcher.StartedDaemon";
    public const string LauncherImportSummary = "Launcher.ImportSummary";
    public const string LauncherImportSkipped = "Launcher.ImportSkipped";
    public const string LauncherStartedCharacter = "Launcher.StartedCharacter";
    public const string LauncherCharactersHeader = "Launcher.CharactersHeader";
    public const string LauncherCharacterLine = "Launcher.CharacterLine";
    public const string LauncherSettingsSaved = "Launcher.SettingsSaved";
    public const string LauncherRestartToApply = "Launcher.RestartToApply";
    public const string LauncherStatusTitle = "Launcher.StatusTitle";
    public const string LauncherStatusGlobal = "Launcher.StatusGlobal";
    public const string LauncherStatusLibrary = "Launcher.StatusLibrary";
    public const string LauncherStatusLibraryDaemon = "Launcher.StatusLibraryDaemon";
    public const string LauncherStatusCharacter = "Launcher.StatusCharacter";
    public const string LauncherStatusCharacterState = "Launcher.StatusCharacterState";
    public const string LauncherStatusNoCharacter = "Launcher.StatusNoCharacter";
    public const string LauncherStatusLibraries = "Launcher.StatusLibraries";
    public const string LauncherStatusLibraryHeader = "Launcher.StatusLibraryHeader";
    public const string LauncherStatusLibraryDaemonLine = "Launcher.StatusLibraryDaemonLine";
    public const string LauncherStatusCharactersLine = "Launcher.StatusCharactersLine";
    public const string LauncherStatusRuntimeLine = "Launcher.StatusRuntimeLine";
    public const string LauncherStatusOpen = "Launcher.Status.Open";
    public const string LauncherStatusClosed = "Launcher.Status.Closed";
    public const string LauncherStatusOn = "Launcher.Status.On";
    public const string LauncherStatusOff = "Launcher.Status.Off";
    public const string LauncherStatusRunning = "Launcher.Status.Running";
    public const string LauncherStatusVersionSuffix = "Launcher.Status.VersionSuffix";
    public const string LauncherStatusDetailSuffix = "Launcher.Status.DetailSuffix";
    public const string LauncherStatusNotRunning = "Launcher.Status.NotRunning";
    public const string LauncherStatusStarting = "Launcher.Status.Starting";
    public const string LauncherStatusPlaceholder = "Launcher.Status.Placeholder";

    public const string LanguageSimplifiedChinese = "Language.zh-Hans";
    public const string LanguageEnglish = "Language.en";
    public const string LanguageJapanese = "Language.ja";
    public const string LanguageKorean = "Language.ko";
}