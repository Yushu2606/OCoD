using System.Diagnostics;

using OCoD.Protocol;

using LibraryClientApi = OCoD.LibraryClient.LibraryClient;

namespace OCoD.Launcher;

internal static class Program
{
    private static readonly TimeSpan DaemonStartupTimeout = TimeSpan.FromSeconds(25);

    public static async Task<int> Main(string[] args)
    {
        if (!LauncherOptions.TryParse(args, out var options, out var error) || options is null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(LauncherOptions.Usage);
            return 2;
        }

        if (options.Command == LauncherCommand.Help)
        {
            Console.WriteLine(LauncherOptions.Usage);
            return 0;
        }

        var runtimeRoot = OcodPaths.ResolveRuntimeRoot(options.DataDirectory);
        var libraryPath = OcodPaths.ResolveDefaultLibrary(
            AppContext.BaseDirectory,
            options.LibraryPath);
        string? smokeRoot = null;

        if (options.Command == LauncherCommand.SmokeTest)
        {
            smokeRoot = Path.Combine(
                Path.GetTempPath(),
                "ocod-smoke-" + Guid.NewGuid().ToString("N")[..8]);
            runtimeRoot = Path.Combine(smokeRoot, "data");
            libraryPath = Path.Combine(smokeRoot, "library");
        }

        Directory.CreateDirectory(OcodPaths.LogsDirectory(runtimeRoot));
        using var log = LogFile.TryOpen(OcodPaths.LauncherLogFile(runtimeRoot));

        void Log(string message)
        {
            Console.WriteLine(message);
            log?.Write(message);
        }

        var context = new LauncherContext(
            options,
            runtimeRoot,
            libraryPath,
            smokeRoot,
            Log);

        try
        {
            return options.Command switch
            {
                LauncherCommand.Status => await PrintStatusAsync(context).ConfigureAwait(false),
                LauncherCommand.Stop => await StopDaemonAsync(context).ConfigureAwait(false),
                LauncherCommand.SmokeTest => await TopologySmokeTest.RunAsync(context)
                    .ConfigureAwait(false),
                LauncherCommand.Import => await ImportCharacterAsync(context).ConfigureAwait(false),
                LauncherCommand.Characters => await ListCharactersAsync(context).ConfigureAwait(false),
                LauncherCommand.SetSettings => await SetSettingsAsync(context).ConfigureAwait(false),
                _ => await RunDaemonCommandAsync(context).ConfigureAwait(false),
            };
        }
        catch (Exception exception)
        {
            Log($"launcher failed: {exception}");
            return 1;
        }
    }

    private static async Task<int> RunDaemonCommandAsync(LauncherContext context)
    {
        await using var connection = await EnsureDaemonAsync(context).ConfigureAwait(false);

        var method = context.Options.Command switch
        {
            LauncherCommand.Start or LauncherCommand.EnsureRuntime => "ensure-runtime",
            LauncherCommand.OpenManagement => "open-management",
            LauncherCommand.StopRuntime => "stop-runtime",
            LauncherCommand.RestartRuntime => "restart-runtime",
            LauncherCommand.RestartLibraryDaemon => "restart-library-daemon",
            LauncherCommand.StartCharacter => "start-character",
            LauncherCommand.StopCharacter => "stop-character",
            LauncherCommand.ActivateCharacter => "activate-character",
            _ => throw new InvalidOperationException(
                $"Unsupported launcher command {context.Options.Command}."),
        };

        System.Text.Json.JsonElement? parameters = context.Options.Command switch
        {
            LauncherCommand.StartCharacter or LauncherCommand.StopCharacter
                or LauncherCommand.ActivateCharacter => ProtocolJson.ToElement(
                    new CharacterIdRequest(context.Options.CharacterId!, context.LibraryPath)),
            LauncherCommand.Start or LauncherCommand.EnsureRuntime
                or LauncherCommand.OpenManagement or LauncherCommand.StopRuntime
                or LauncherCommand.RestartRuntime
                or LauncherCommand.RestartLibraryDaemon => ProtocolJson.ToElement(
                    new LibraryPathRequest(context.LibraryPath)),
            _ => null,
        };

        var response = await connection.InvokeAsync(
            method,
            parameters,
            timeout: TimeSpan.FromSeconds(60)).ConfigureAwait(false);

        if (response.Ok == false)
        {
            Console.Error.WriteLine(response.Error ?? $"Command '{method}' failed.");
            return 2;
        }

        var status = await GetStatusAsync(connection).ConfigureAwait(false);
        PrintStatus(status);
        return 0;
    }

    private static async Task<int> PrintStatusAsync(LauncherContext context)
    {
        var endpoint = EndpointPaths.ForGlobalDaemon(context.RuntimeRoot);

        await using var connection = await TryConnectAsync(endpoint, TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);

        if (connection is null)
        {
            Console.WriteLine("OCoD global daemon is not running.");
            return 1;
        }

        PrintStatus(await GetStatusAsync(connection).ConfigureAwait(false));
        return 0;
    }

    private static async Task<int> StopDaemonAsync(LauncherContext context)
    {
        var endpoint = EndpointPaths.ForGlobalDaemon(context.RuntimeRoot);
        var connection = await TryConnectAsync(endpoint, TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);

        if (connection is null)
        {
            Console.WriteLine("OCoD global daemon is not running.");
            return 0;
        }

        await using (connection.ConfigureAwait(false))
        {
            var response = await connection.InvokeAsync(
                "shutdown",
                parameters: null,
                timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            if (response.Ok == false)
            {
                Console.Error.WriteLine(response.Error ?? "Shutdown request failed.");
                return 2;
            }
        }

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var probe = await TryConnectAsync(endpoint, TimeSpan.FromMilliseconds(500))
                .ConfigureAwait(false);

            if (probe is null)
            {
                Console.WriteLine("OCoD stopped.");
                return 0;
            }

            await Task.Delay(300).ConfigureAwait(false);
        }

        Console.Error.WriteLine("Timed out waiting for the global daemon to stop.");
        return 2;
    }

    internal static async Task<IpcClientConnection> EnsureDaemonAsync(LauncherContext context)
    {
        var endpoint = EndpointPaths.ForGlobalDaemon(context.RuntimeRoot);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var existing = await TryConnectAsync(endpoint, TimeSpan.FromMilliseconds(500))
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return existing;
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        var command = ComponentLocator.Resolve(
            "OCoD.GlobalDaemon",
            context.Options.ComponentsDirectory);

        var arguments = new List<string>
        {
            "--library",
            context.LibraryPath,
            "--data-dir",
            context.RuntimeRoot,
        };

        if (context.Options.ComponentsDirectory is { } componentsDirectory)
        {
            arguments.Add("--components-dir");
            arguments.Add(componentsDirectory);
        }

        if (context.Options.LanguageTag is { } languageTag)
        {
            arguments.Add("--lang");
            arguments.Add(languageTag);
        }

        StartDetached(command, arguments, context.Log);

        var deadline = DateTimeOffset.UtcNow + DaemonStartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var connection = await TryConnectAsync(endpoint, TimeSpan.FromSeconds(1))
                .ConfigureAwait(false);

            if (connection is not null)
            {
                return connection;
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        throw new TimeoutException("The global daemon did not become ready.");
    }

    internal static async Task<IpcClientConnection?> TryConnectAsync(
        string endpoint,
        TimeSpan timeout)
    {
        try
        {
            return await IpcClientConnection.ConnectAsync(endpoint, timeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException
                or System.Net.Sockets.SocketException
                or IpcException
                or TimeoutException
                or OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<int> ImportCharacterAsync(LauncherContext context)
    {
        var importPath = Path.GetFullPath(
            context.Options.ImportPath
            ?? throw new InvalidOperationException("An import path is required."));

        await using var connection = await EnsureDaemonAsync(context).ConfigureAwait(false);
        await EnsureLibraryAsync(connection, context).ConfigureAwait(false);
        await using var library = await ConnectLibraryAsync(
            context,
            TimeSpan.FromSeconds(20)).ConfigureAwait(false);

        var result = await library.ImportCvclkAsync(importPath).ConfigureAwait(false);
        Console.WriteLine(
            $"imported {result.Imported.Count} character(s), skipped {result.Skipped.Count}");

        foreach (var skipped in result.Skipped)
        {
            Console.WriteLine(
                $"  skipped {skipped.Name ?? skipped.SourceId ?? "<unknown>"}: {skipped.Reason}");
        }

        foreach (var imported in result.Imported)
        {
            var response = await connection.InvokeAsync(
                "start-character",
                ProtocolJson.ToElement(new CharacterIdRequest(imported.Id, context.LibraryPath)),
                timeout: TimeSpan.FromSeconds(60)).ConfigureAwait(false);

            if (response.Ok == false)
            {
                Console.Error.WriteLine(
                    $"failed to start {imported.Name}: {response.Error}");
                continue;
            }

            Console.WriteLine($"started {imported.Name} ({imported.Id})");
        }

        PrintStatus(await GetStatusAsync(connection).ConfigureAwait(false));
        return result.Imported.Count == 0 && result.Skipped.Count > 0 ? 1 : 0;
    }

    private static async Task<int> ListCharactersAsync(LauncherContext context)
    {
        await using var connection = await EnsureDaemonAsync(context).ConfigureAwait(false);
        await EnsureLibraryAsync(connection, context).ConfigureAwait(false);
        await using var library = await ConnectLibraryAsync(
            context,
            TimeSpan.FromSeconds(20)).ConfigureAwait(false);

        var characters = await library.ListCharactersAsync(
            timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Console.WriteLine($"characters in {context.LibraryPath}: {characters.Count}");
        foreach (var character in characters)
        {
            Console.WriteLine(
                $"  {character.Id}  {character.Name}  " +
                $"startSize={(character.StartSize == 0 ? 160 : character.StartSize)}");
        }

        return 0;
    }

    private static async Task EnsureLibraryAsync(
        IpcClientConnection connection,
        LauncherContext context)
    {
        var response = await connection.InvokeAsync(
            "ensure-library",
            ProtocolJson.ToElement(new LibraryPathRequest(context.LibraryPath)),
            timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        if (response.Ok == false)
        {
            throw new IpcException(
                response.Error ?? $"Could not start the library daemon for '{context.LibraryPath}'.");
        }
    }

    private static async Task<LibraryClientApi> ConnectLibraryAsync(
        LauncherContext context,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                return await LibraryClientApi.ConnectAsync(
                    context.LibraryPath,
                    ComponentNames.Launcher,
                    timeout: TimeSpan.FromSeconds(2)).ConfigureAwait(false);
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

        throw new TimeoutException(
            $"The library daemon for '{context.LibraryPath}' did not become ready: " +
            $"{lastError?.Message}");
    }

    private static async Task<int> SetSettingsAsync(LauncherContext context)
    {
        var characterId = context.Options.CharacterId
            ?? throw new InvalidOperationException("A character id is required.");

        await using var connection = await EnsureDaemonAsync(context).ConfigureAwait(false);
        await EnsureLibraryAsync(connection, context).ConfigureAwait(false);
        await using var library = await ConnectLibraryAsync(
            context,
            TimeSpan.FromSeconds(20)).ConfigureAwait(false);

        var character = await library.GetCharacterAsync(characterId).ConfigureAwait(false);
        var current = character.Settings;

        var settings = new CharacterRunSettings(
            context.Options.Width ?? current.Width,
            context.Options.Height ?? current.Height,
            current.X,
            current.Y,
            context.Options.Flipped ?? current.Flipped,
            context.Options.Topmost ?? current.Topmost,
            current.Locked);

        await library.SaveRunSettingsAsync(characterId, settings).ConfigureAwait(false);
        Console.WriteLine(
            $"saved settings for {character.Name}: width={settings.Width}, " +
            $"height={settings.Height}, flipped={settings.Flipped}, topmost={settings.Topmost}");
        Console.WriteLine("Restart the character to apply the saved settings.");
        return 0;
    }

    internal static async Task<GlobalStatus> GetStatusAsync(IpcClientConnection connection)
    {
        var response = await connection.InvokeAsync(
            "status",
            parameters: null,
            timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        return response.Result is { } result
            ? ProtocolJson.FromElement<GlobalStatus>(result)
            : throw new IpcException("The global daemon returned no status.");
    }

    internal static void PrintStatus(GlobalStatus status)
    {
        Console.WriteLine("OCoD topology:");
        Console.WriteLine(
            $"  global daemon   : pid {status.Pid}, version {status.Version}, " +
            $"management window {(status.ManagementWindowOpen ? "open" : "closed")}");
        Console.WriteLine($"  library         : {status.LibraryPath}");
        Console.WriteLine($"  library daemon  : {Describe(status.LibraryDaemon)}");

        if (status.CharacterRuntimes is { Count: > 0 } runtimes)
        {
            foreach (var runtime in runtimes)
            {
                var label = runtime.CharacterName ?? runtime.CharacterId ?? "placeholder";
                Console.WriteLine($"  character       : {label} — {Describe(runtime.Process)}");

                if (runtime.State is { } state)
                {
                    Console.WriteLine(
                        $"  character state : click-through={(state.ClickThrough ? "on" : "off")}, " +
                        $"library-connected={state.LibraryConnected}, flipped={state.Flipped}, " +
                        $"locked={state.Locked}, topmost={state.Topmost}");
                }
            }
        }
        else
        {
            Console.WriteLine($"  character       : {Describe(status.CharacterRuntime)}");
        }

        if (status.Libraries is { Count: > 1 } libraries)
        {
            Console.WriteLine($"  libraries       : {libraries.Count}");

            foreach (var library in libraries)
            {
                Console.WriteLine(
                    $"    {library.DisplayName}  {library.LibraryPath}  " +
                    $"[management {(library.ManagementWindowOpen ? "open" : "closed")}]");
                Console.WriteLine($"      library daemon : {Describe(library.LibraryDaemon)}");
                Console.WriteLine($"      characters     : {library.CharacterRuntimes.Count}");

                foreach (var runtime in library.CharacterRuntimes)
                {
                    var label = runtime.CharacterName ?? runtime.CharacterId ?? "placeholder";
                    Console.WriteLine($"        {label} — {Describe(runtime.Process)}");
                }
            }
        }
    }

    private static string Describe(ProcessInfo process)
    {
        if (!process.Running)
        {
            return process.Detail ?? "stopped";
        }

        var version = process.Version is null ? string.Empty : $", version {process.Version}";
        var detail = process.Detail is null ? string.Empty : $", {process.Detail}";
        return $"running, pid {process.Pid}{version}{detail}";
    }

    private static void StartDetached(
        ComponentCommand command,
        IReadOnlyList<string> arguments,
        Action<string> log)
    {
        ProcessStartInfo startInfo;

        if (OperatingSystem.IsWindows())
        {
            startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in command.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        else
        {
            // Detach stdio so a launcher started in a pipeline does not keep that pipe open for the
            // lifetime of the daemon. The daemon writes its own log file.
            startInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("exec \"$0\" \"$@\" >/dev/null 2>&1");
            startInfo.ArgumentList.Add(command.FileName);

            foreach (var argument in command.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the global daemon.");

        log($"started global daemon as pid {process.Id}");
    }
}

internal sealed record LauncherContext(
    LauncherOptions Options,
    string RuntimeRoot,
    string LibraryPath,
    string? SmokeRoot,
    Action<string> Log);