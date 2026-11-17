using System.Net.Sockets;
using System.Runtime.InteropServices;

using OCoD.Protocol;

using LibraryClientApi = OCoD.LibraryClient.LibraryClient;

namespace OCoD.LibraryDaemon;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!LibraryDaemonOptions.TryParse(args, out var options, out var error) || options is null)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var libraryPath = options.LibraryPath;
        var endpoint = EndpointPaths.ForLibrary(libraryPath);

        if (!SingleInstanceLock.TryAcquire(
                OcodPaths.LibraryLockFile(libraryPath),
                out var instanceLock,
                out var lockError))
        {
            return await ExitIfAlreadyRunningAsync(libraryPath, lockError).ConfigureAwait(false);
        }

        using (instanceLock)
        {
            using var log = LogFile.TryOpen(OcodPaths.LibraryLogFile(libraryPath));

            void Log(string message)
            {
                Console.WriteLine(message);
                log?.Write(message);
            }

            try
            {
                var store = new LibraryStore(libraryPath);
                await store.InitializeAsync().ConfigureAwait(false);

                var characters = new CharacterLibrary(libraryPath, Log);
                await characters.InitializeAsync().ConfigureAwait(false);

                var server = new LibraryDaemonServer(
                    store,
                    characters,
                    libraryPath,
                    endpoint,
                    options.IdleExitDelay,
                    Log);

                server.Start();
                Log($"library daemon ready for '{libraryPath}' (library {store.LibraryId})");

                using var sigterm = TryRegisterSignal(PosixSignal.SIGTERM, server, "SIGTERM");
                using var sigint = TryRegisterSignal(PosixSignal.SIGINT, server, "SIGINT");

                Console.CancelKeyPress += (_, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    _ = server.StopAsync("Ctrl+C");
                };

                var reason = await server.ShutdownRequested.ConfigureAwait(false);
                await server.DisposeAsync().ConfigureAwait(false);
                await store.FlushAsync().ConfigureAwait(false);
                Log($"library daemon exiting: {reason}");
                return 0;
            }
            catch (Exception exception)
            {
                Log($"fatal: {exception}");
                return 1;
            }
        }
    }

    private static async Task<int> ExitIfAlreadyRunningAsync(
        string libraryPath,
        Exception? lockError)
    {
        Exception? lastError = lockError;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await using var client = await LibraryClientApi.ConnectAsync(
                    libraryPath,
                    ComponentNames.LibraryDaemon,
                    timeout: TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                Console.WriteLine(
                    $"library daemon already running for '{libraryPath}' (pid {client.Server.Pid})");
                return 0;
            }
            catch (Exception exception) when (
                exception is IOException
                    or SocketException
                    or IpcException
                    or TimeoutException
                    or OperationCanceledException)
            {
                lastError = exception;
                await Task.Delay(200).ConfigureAwait(false);
            }
        }

        Console.Error.WriteLine(
            $"another library daemon holds the lock for '{libraryPath}', but it is unreachable: " +
            $"{lastError?.Message}");
        return 3;
    }

    private static PosixSignalRegistration? TryRegisterSignal(
        PosixSignal signal,
        LibraryDaemonServer server,
        string name)
    {
        try
        {
            return PosixSignalRegistration.Create(signal, context =>
            {
                context.Cancel = true;
                _ = server.StopAsync(name);
            });
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }
}