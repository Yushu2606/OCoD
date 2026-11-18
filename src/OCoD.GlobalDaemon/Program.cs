using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using OCoD.Desktop;
using OCoD.Localization;
using OCoD.Protocol;

namespace OCoD.GlobalDaemon;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!GlobalDaemonOptions.TryParse(args, out var options, out var error) || options is null)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        Directory.CreateDirectory(OcodPaths.LogsDirectory(options.RuntimeRoot));
        using var log = LogFile.TryOpen(OcodPaths.GlobalDaemonLogFile(options.RuntimeRoot));

        void Log(string message)
        {
            Console.WriteLine(message);
            log?.Write(message);
        }

        var requestedCulture = ResolveRequestedCulture(options.LanguageTag, out var invalidLanguageTag);
        var localization = LocalizationService.CreateDefault(requestedCulture);

        if (invalidLanguageTag is not null)
        {
            Log(localization.Get(LocKeys.WarningUnrecognizedLanguage, invalidLanguageTag));
        }

        if (!SingleInstanceLock.TryAcquire(
                OcodPaths.GlobalDaemonLockFile(options.RuntimeRoot),
                out var instanceLock,
                out var lockError))
        {
            Log($"global daemon lock is held ({lockError?.Message}); activating the existing daemon");
            return ActivateExistingDaemonAsync(options.RuntimeRoot, localization)
                .GetAwaiter()
                .GetResult();
        }

        using (instanceLock)
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime
            {
                Args = args,
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
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

            var service = new GlobalDaemonService(options, localization, lifetime, Log);

            if (appBuilder.Instance is { } application)
            {
                TrayIcon.SetIcons(application, new TrayIcons { service.TrayIcon });
            }

            service.Start();

            try
            {
                lifetime.Start(args);
                return Environment.ExitCode;
            }
            finally
            {
                service.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static async Task<int> ActivateExistingDaemonAsync(
        string runtimeRoot,
        LocalizationService localization)
    {
        var endpoint = EndpointPaths.ForGlobalDaemon(runtimeRoot);
        Exception? lastError = null;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await using var connection = await IpcClientConnection.ConnectAsync(
                    endpoint,
                    TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                var response = await connection.InvokeAsync(
                    "open-management",
                    parameters: null,
                    timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                if (response.Ok == false)
                {
                    Console.Error.WriteLine(
                        response.Error ?? localization.Get(LocKeys.DaemonOpenManagementFailed));
                    return 2;
                }

                Console.WriteLine(localization.Get(LocKeys.DaemonAlreadyRunning));
                return 0;
            }
            catch (Exception exception) when (
                exception is IOException
                    or System.Net.Sockets.SocketException
                    or IpcException
                    or TimeoutException
                    or OperationCanceledException)
            {
                lastError = exception;
                await Task.Delay(200).ConfigureAwait(false);
            }
        }

        Console.Error.WriteLine(
            localization.Get(LocKeys.DaemonControlUnreachable, lastError?.Message));
        return 2;
    }

    private static CultureInfo? ResolveRequestedCulture(
        string? languageTag,
        out string? invalidLanguageTag)
    {
        invalidLanguageTag = null;

        if (languageTag is null)
        {
            return null;
        }

        if (SupportedCultures.TryParse(languageTag, out var culture))
        {
            return culture;
        }

        invalidLanguageTag = languageTag;
        return null;
    }
}