using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using OCoD.Desktop;
using OCoD.Localization;
using OCoD.Protocol;

namespace OCoD.CharacterHost;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = CharacterHostOptions.Parse(args, Environment.GetEnvironmentVariable("OCOD_LANG"));
        var localization = LocalizationService.CreateDefault(ResolveRequestedCulture(options.LanguageTag));

        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnLastWindowClose,
        };

        var appBuilder = AppBuilder.Configure<Application>()
            .UsePlatformDetect()
            .AfterSetup(builder =>
            {
                if (builder.Instance is { } application)
                {
                    DesktopAssets.ConfigureApplication(application, localization);
                }
            })
            .SetupWithLifetime(lifetime);

        var shell = new CharacterShell(
            localization,
            lifetime,
            options.SmokeTest,
            createTray: options.ControlEndpoint is null);

        lifetime.MainWindow = shell.Window;

        CharacterControlClient? control = null;
        LibraryRuntimeConnection? libraryConnection = null;

        try
        {
            if (options.ControlEndpoint is not null)
            {
                if (options.ControlToken is null)
                {
                    Console.Error.WriteLine("OCoD: --control-endpoint requires --control-token.");
                    return 2;
                }

                try
                {
                    control = CharacterControlClient.ConnectAsync(
                            options.ControlEndpoint,
                            options.ControlToken,
                            command => Dispatcher.UIThread.Post(
                                () => shell.HandleControlCommand(command)))
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"OCoD: could not connect to the global daemon: {exception.Message}");
                    return 2;
                }

                shell.AttachControl(control);
            }

            if (options.LibraryPath is not null)
            {
                libraryConnection = new LibraryRuntimeConnection(
                    options.LibraryPath,
                    _ => Dispatcher.UIThread.Post(shell.PublishState));
                shell.AttachLibraryConnection(libraryConnection);
            }

            if (options.CharacterId is not null)
            {
                if (libraryConnection is null)
                {
                    Console.Error.WriteLine("OCoD: --character requires --library.");
                    return 2;
                }

                try
                {
                    LoadCharacter(shell, libraryConnection, options.CharacterId);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"OCoD: could not load character '{options.CharacterId}': {exception}");
                    return 3;
                }
            }

            if (appBuilder.Instance is { } application && shell.TrayIcon is { } tray)
            {
                TrayIcon.SetIcons(application, new TrayIcons { tray });
            }

            lifetime.Exit += (_, _) => shell.Dispose();
            lifetime.Start(args);
            return Environment.ExitCode;
        }
        finally
        {
            shell.Dispose();

            if (control is not null)
            {
                control.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            if (libraryConnection is not null)
            {
                libraryConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static void LoadCharacter(
        CharacterShell shell,
        LibraryRuntimeConnection libraryConnection,
        string characterId)
    {
        libraryConnection.WaitForConnectedAsync(TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult();

        var record = libraryConnection.GetCharacterAsync(characterId)
            .GetAwaiter()
            .GetResult();

        var slotMap = new (string ViewSlot, string RecordSlot)[]
        {
            ("body", "body"),
            ("head", "head1"),
            ("back", "back1"),
            ("side", "side1"),
            ("ex1", "ex1"),
            ("ex2", "ex2"),
            ("ex3", "ex3"),
            ("ex4", "ex4"),
        };

        var bitmaps = new Dictionary<string, Bitmap>(StringComparer.Ordinal);

        try
        {
            foreach (var (viewSlot, recordSlot) in slotMap)
            {
                if (!record.Images.Any(image =>
                        string.Equals(image.Slot, recordSlot, StringComparison.Ordinal)))
                {
                    continue;
                }

                var bytes = libraryConnection.GetCharacterImageAsync(characterId, recordSlot)
                    .GetAwaiter()
                    .GetResult();
                bitmaps[viewSlot] = new Bitmap(new MemoryStream(bytes, writable: false));
            }

            if (!bitmaps.ContainsKey("body") && !bitmaps.ContainsKey("head"))
            {
                throw new InvalidDataException("The character has no displayable body or head image.");
            }

            shell.LoadCharacter(record, bitmaps);
        }
        catch
        {
            foreach (var bitmap in bitmaps.Values)
            {
                bitmap.Dispose();
            }

            throw;
        }
    }

    private static CultureInfo? ResolveRequestedCulture(string? languageTag)
    {
        if (languageTag is null)
        {
            return null;
        }

        if (SupportedCultures.TryParse(languageTag, out var culture))
        {
            return culture;
        }

        Console.Error.WriteLine($"OCoD: ignoring unrecognized language tag '{languageTag}'.");
        return null;
    }
}