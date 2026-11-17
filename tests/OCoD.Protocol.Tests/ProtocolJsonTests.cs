using OCoD.Protocol;

namespace OCoD.Protocol.Tests;

public sealed class ProtocolJsonTests
{
    [Fact]
    public void Character_command_roundtrips_language_and_click_through()
    {
        var command = new CharacterCommand(
            CharacterCommands.SetLanguage,
            Enabled: null,
            LanguageTag: "ja");

        var roundtrip = ProtocolJson.FromElement<CharacterCommand>(
            ProtocolJson.ToElement(command));

        Assert.Equal(CharacterCommands.SetLanguage, roundtrip.Name);
        Assert.Null(roundtrip.Enabled);
        Assert.Equal("ja", roundtrip.LanguageTag);
    }

    [Fact]
    public void Global_status_roundtrips_process_topology()
    {
        var status = new GlobalStatus(
            Pid: 123,
            Version: "0.1.0",
            RuntimeRoot: "/tmp/ocod-runtime",
            LibraryPath: "/tmp/ocod-library",
            ManagementWindowOpen: true,
            LibraryDaemon: new ProcessInfo("library-daemon", true, 456, "0.1.0", null),
            CharacterRuntime: new ProcessInfo("character-runtime", true, 789, "0.1.0", null),
            CharacterState: new CharacterState(
                Visible: true,
                ClickThrough: false,
                Locked: true,
                Topmost: false,
                Flipped: true,
                LibraryConnected: true,
                Pid: 789,
                Version: "0.1.0",
                LibraryPath: "/tmp/ocod-library"));

        var roundtrip = ProtocolJson.FromElement<GlobalStatus>(
            ProtocolJson.ToElement(status));

        Assert.Equal(status, roundtrip);
    }

    [Fact]
    public void Library_status_roundtrips_clients()
    {
        var status = new LibraryStatus(
            LibraryPath: "/tmp/ocod-library",
            LibraryId: "abc",
            CharacterCount: 0,
            DaemonPid: 456,
            DaemonVersion: "0.1.0",
            StartedAtUtc: DateTimeOffset.Parse("2026-11-17T00:00:00Z"),
            PendingWrites: 0,
            Clients:
            [
                new LibraryClientInfo(1, "character-runtime", 789, DateTimeOffset.Parse("2026-11-17T00:00:01Z")),
                new LibraryClientInfo(2, "management-window", 123, DateTimeOffset.Parse("2026-11-17T00:00:02Z")),
            ]);

        var roundtrip = ProtocolJson.FromElement<LibraryStatus>(
            ProtocolJson.ToElement(status));

        Assert.Equal(status.LibraryPath, roundtrip.LibraryPath);
        Assert.Equal(status.LibraryId, roundtrip.LibraryId);
        Assert.Equal(status.CharacterCount, roundtrip.CharacterCount);
        Assert.Equal(status.DaemonPid, roundtrip.DaemonPid);
        Assert.Equal(status.DaemonVersion, roundtrip.DaemonVersion);
        Assert.Equal(status.StartedAtUtc, roundtrip.StartedAtUtc);
        Assert.Equal(status.PendingWrites, roundtrip.PendingWrites);
        Assert.Equal(status.Clients, roundtrip.Clients);
    }

    [Fact]
    public void Ipc_message_roundtrips_params_and_result()
    {
        var message = new IpcMessage
        {
            Id = "7",
            Method = "hello",
            Ok = true,
            Params = ProtocolJson.ToElement(new HelloRequest("test", 12, "0.1.0")),
            Result = ProtocolJson.ToElement(new CommandResult(true, "ok")),
        };

        var line = ProtocolJson.Serialize(message);
        var roundtrip = ProtocolJson.Parse(line);

        Assert.Equal("7", roundtrip.Id);
        Assert.Equal("hello", roundtrip.Method);
        Assert.True(roundtrip.Ok);
        Assert.NotNull(roundtrip.Params);
        Assert.NotNull(roundtrip.Result);
    }
}