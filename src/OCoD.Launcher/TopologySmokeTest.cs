using OCoD.Protocol;

using LibraryClientApi = OCoD.LibraryClient.LibraryClient;

namespace OCoD.Launcher;

internal static class TopologySmokeTest
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static async Task<int> RunAsync(LauncherContext context)
    {
        var failures = new List<string>();

        void Check(bool condition, string description)
        {
            context.Log($"{(condition ? "ok" : "FAIL")}: {description}");
            if (!condition)
            {
                failures.Add(description);
            }
        }

        ManagedProcess? daemon = null;

        try
        {
            var command = ComponentLocator.Resolve(
                "OCoD.GlobalDaemon",
                context.Options.ComponentsDirectory);
            var arguments = new List<string>
            {
                "--library",
                context.LibraryPath,
                "--data-dir",
                context.RuntimeRoot,
                "--lang",
                "en",
            };

            if (context.Options.ComponentsDirectory is { } componentsDirectory)
            {
                arguments.Add("--components-dir");
                arguments.Add(componentsDirectory);
            }

            daemon = ManagedProcess.Start(
                command,
                arguments,
                line => context.Log($"[global-daemon] {line}"));

            var endpoint = EndpointPaths.ForGlobalDaemon(context.RuntimeRoot);
            var connection = await ConnectAsync(endpoint, TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);
            Check(connection is not null, "global daemon control endpoint becomes ready");

            if (connection is null)
            {
                return Finish(context, failures);
            }

            await using (connection.ConfigureAwait(false))
            {
                var status = await WaitForStatusAsync(
                    connection,
                    current => current.LibraryDaemon.Running
                        && current.CharacterRuntime.Running
                        && current.CharacterState?.LibraryConnected == true,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                Check(status is not null, "global daemon starts the library daemon and character runtime");
                Check(
                    status?.LibraryDaemon.Running == true,
                    "library daemon is running");
                Check(
                    status?.CharacterRuntime.Running == true,
                    "character runtime is running");
                Check(
                    status?.CharacterState?.LibraryConnected == true,
                    "character runtime is connected to the library daemon");

                var libraryStatus = await WaitForLibraryClientsAsync(
                    context.LibraryPath,
                    clients => clients.Any(client =>
                        client.ClientName == ComponentNames.CharacterRuntime),
                    TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Check(libraryStatus is not null, "library daemon answers status requests");
                Check(
                    libraryStatus is not null,
                    "library daemon lists the character runtime as a client");

                var libraryDaemonPid = status?.LibraryDaemon.Pid;
                Check(libraryDaemonPid is not null, "library daemon exposes its pid");

                var secondDaemon = ManagedProcess.Start(
                    ComponentLocator.Resolve(
                        "OCoD.LibraryDaemon",
                        context.Options.ComponentsDirectory),
                    ["--library", context.LibraryPath],
                    line => context.Log($"[second-library-daemon] {line}"));

                var secondExited = await secondDaemon.WaitForExitAsync(
                    TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                Check(secondExited, "a second library daemon exits instead of becoming a writer");
                Check(
                    secondExited && secondDaemon.ExitCode == 0,
                    "the second library daemon reports the existing instance and exits cleanly");
                await secondDaemon.DisposeAsync().ConfigureAwait(false);

                var afterSecond = await QueryLibraryAsync(context.LibraryPath)
                    .ConfigureAwait(false);
                Check(
                    afterSecond?.DaemonPid == libraryDaemonPid,
                    "the original library daemon remains the only live instance");

                await InvokeAsync(connection, "stop-runtime").ConfigureAwait(false);
                var stopped = await WaitForStatusAsync(
                    connection,
                    current => !current.CharacterRuntime.Running,
                    TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Check(stopped is not null, "character runtime can be stopped independently");

                var libraryStopped = await WaitForStatusAsync(
                    connection,
                    current => !current.LibraryDaemon.Running,
                    TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Check(
                    libraryStopped is not null,
                    "library daemon exits after its last client disconnects");

                await InvokeAsync(connection, "ensure-runtime").ConfigureAwait(false);
                var restarted = await WaitForStatusAsync(
                    connection,
                    current => current.CharacterRuntime.Running && current.LibraryDaemon.Running,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Check(restarted is not null, "character runtime can be restarted on demand");
                Check(
                    restarted is not null
                        && restarted.LibraryDaemon.Pid is { } restartedLibraryPid
                        && restartedLibraryPid != libraryDaemonPid,
                    "restarting the character runtime starts a fresh library daemon");

                await SetClickThroughAsync(connection, enabled: true).ConfigureAwait(false);
                var clickThroughOn = await WaitForStatusAsync(
                    connection,
                    current => current.CharacterState?.ClickThrough == true,
                    TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Check(clickThroughOn is not null, "global daemon can enable click-through in the runtime");

                await SetClickThroughAsync(connection, enabled: false).ConfigureAwait(false);
                var clickThroughOff = await WaitForStatusAsync(
                    connection,
                    current => current.CharacterState?.ClickThrough == false,
                    TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Check(clickThroughOff is not null, "global daemon can restore interaction in the runtime");

                await InvokeAsync(connection, "open-management").ConfigureAwait(false);
                var managementOpen = await WaitForStatusAsync(
                    connection,
                    current => current.ManagementWindowOpen,
                    TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Check(managementOpen is not null, "the management window can be opened");

                var withManagement = await WaitForLibraryClientsAsync(
                    context.LibraryPath,
                    clients => clients.Any(client => client.ClientName == ComponentNames.ManagementWindow)
                        && clients.Any(client => client.ClientName == ComponentNames.CharacterRuntime),
                    TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Check(
                    withManagement is not null,
                    "management window and character runtime share the library daemon");

                var secondLibraryPath = Path.Combine(context.SmokeRoot!, "library-b");
                await InvokeAsync(
                    connection,
                    "open-management",
                    ProtocolJson.ToElement(new LibraryPathRequest(secondLibraryPath)))
                    .ConfigureAwait(false);

                var secondLibrary = await WaitForStatusAsync(
                    connection,
                    current => FindLibrary(current, secondLibraryPath) is
                    { ManagementWindowOpen: true, LibraryDaemon.Running: true },
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Check(secondLibrary is not null, "a second library can be opened alongside the first");
                Check(
                    secondLibrary is not null
                        && FindLibrary(secondLibrary, context.LibraryPath) is
                        { ManagementWindowOpen: true } firstLibrary
                        && firstLibrary.CharacterRuntimes.Any(runtime => runtime.Process.Running),
                    "opening the second library leaves the first library running");

                var firstDaemonPid = FindLibrary(secondLibrary!, context.LibraryPath)?.LibraryDaemon.Pid;
                var secondDaemonPid = FindLibrary(secondLibrary!, secondLibraryPath)?.LibraryDaemon.Pid;
                Check(
                    firstDaemonPid is not null
                        && secondDaemonPid is not null
                        && firstDaemonPid != secondDaemonPid,
                    "each library has its own library daemon");

                await InvokeAsync(
                    connection,
                    "ensure-runtime",
                    ProtocolJson.ToElement(new LibraryPathRequest(secondLibraryPath)))
                    .ConfigureAwait(false);
                var secondRuntime = await WaitForStatusAsync(
                    connection,
                    current => FindLibrary(current, secondLibraryPath)?.CharacterRuntimes
                        .Any(runtime => runtime.Process.Running) == true
                        && FindLibrary(current, context.LibraryPath)?.CharacterRuntimes
                            .Any(runtime => runtime.Process.Running) == true,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Check(
                    secondRuntime is not null,
                    "character runtimes can run in two libraries at the same time");

                await InvokeAsync(
                    connection,
                    "stop-runtime",
                    ProtocolJson.ToElement(new LibraryPathRequest(secondLibraryPath)))
                    .ConfigureAwait(false);
                var secondStopped = await WaitForStatusAsync(
                    connection,
                    current => FindLibrary(current, secondLibraryPath)?.CharacterRuntimes
                        .Any(runtime => runtime.Process.Running) == false
                        && FindLibrary(current, context.LibraryPath)?.CharacterRuntimes
                            .Any(runtime => runtime.Process.Running) == true,
                    TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                Check(
                    secondStopped is not null,
                    "stopping the second library runtime does not affect the first library");

                await InvokeAsync(
                    connection,
                    "close-management",
                    ProtocolJson.ToElement(new LibraryPathRequest(secondLibraryPath)))
                    .ConfigureAwait(false);
                var secondClosed = await WaitForStatusAsync(
                    connection,
                    current => FindLibrary(current, secondLibraryPath) is
                    { ManagementWindowOpen: false }
                        && FindLibrary(current, context.LibraryPath) is
                        { ManagementWindowOpen: true } first
                        && first.CharacterRuntimes.Any(runtime => runtime.Process.Running),
                    TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Check(
                    secondClosed is not null,
                    "closing the second library leaves the first library interactive");

                await InvokeAsync(connection, "shutdown").ConfigureAwait(false);
            }

            var daemonExited = await daemon.WaitForExitAsync(TimeSpan.FromSeconds(20))
                .ConfigureAwait(false);
            Check(daemonExited, "global daemon stops on request");
            Check(
                daemonExited && daemon.ExitCode == 0,
                "global daemon exits with code 0");

            return Finish(context, failures);
        }
        catch (Exception exception)
        {
            context.Log($"smoke test failed with an exception: {exception}");
            failures.Add(exception.ToString());
            return Finish(context, failures);
        }
        finally
        {
            if (daemon is not null)
            {
                await daemon.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static int Finish(LauncherContext context, List<string> failures)
    {
        if (failures.Count == 0)
        {
            context.Log($"OCoD topology smoke test passed. Artifacts: {context.SmokeRoot}");
            return 0;
        }

        context.Log(
            $"OCoD topology smoke test failed: {string.Join("; ", failures)} " +
            $"Artifacts: {context.SmokeRoot}");
        return 1;
    }

    private static async Task<IpcClientConnection?> ConnectAsync(
        string endpoint,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var connection = await Program.TryConnectAsync(
                endpoint,
                TimeSpan.FromSeconds(1)).ConfigureAwait(false);

            if (connection is not null)
            {
                return connection;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<GlobalStatus?> WaitForStatusAsync(
        IpcClientConnection connection,
        Func<GlobalStatus, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var status = await Program.GetStatusAsync(connection).ConfigureAwait(false);
                if (predicate(status))
                {
                    return status;
                }
            }
            catch (Exception)
            {
                return null;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<LibraryStatus?> QueryLibraryAsync(string libraryPath)
    {
        try
        {
            await using var client = await LibraryClientApi.ConnectAsync(
                libraryPath,
                ComponentNames.Launcher,
                timeout: TimeSpan.FromSeconds(3)).ConfigureAwait(false);

            return await client.GetStatusAsync(timeout: TimeSpan.FromSeconds(3))
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<LibraryStatus?> WaitForLibraryClientsAsync(
        string libraryPath,
        Func<List<LibraryClientInfo>, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await QueryLibraryAsync(libraryPath).ConfigureAwait(false);
            if (status is not null && predicate(status.Clients))
            {
                return status;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        return null;
    }

    private static LibraryTopologyStatus? FindLibrary(
        GlobalStatus status,
        string libraryPath) =>
        status.Libraries?.FirstOrDefault(library => PathsEqual(library.LibraryPath, libraryPath));

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }

    private static async Task InvokeAsync(
        IpcClientConnection connection,
        string method,
        System.Text.Json.JsonElement? parameters = null)
    {
        var response = await connection.InvokeAsync(
            method,
            parameters,
            timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        if (response.Ok == false)
        {
            throw new IpcException(response.Error ?? $"Control command '{method}' failed.");
        }
    }

    private static async Task SetClickThroughAsync(IpcClientConnection connection, bool enabled)
    {
        var response = await connection.InvokeAsync(
            "set-click-through",
            ProtocolJson.ToElement(new SetClickThroughRequest(enabled)),
            timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        if (response.Ok == false)
        {
            throw new IpcException(response.Error ?? "Could not change click-through.");
        }
    }
}