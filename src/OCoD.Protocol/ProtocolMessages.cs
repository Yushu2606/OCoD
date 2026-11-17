using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OCoD.Protocol;

public static class ComponentNames
{
    public const string Launcher = "launcher";
    public const string GlobalDaemon = "global-daemon";
    public const string CharacterRuntime = "character-runtime";
    public const string LibraryDaemon = "library-daemon";
    public const string ManagementWindow = "management-window";
}

public static class CharacterCommands
{
    public const string SetClickThrough = "set-click-through";
    public const string SetLanguage = "set-language";
    public const string Show = "show";
    public const string Shutdown = "shutdown";
}

/// <summary>A JSON-line IPC message: request, response, or event.</summary>
public sealed class IpcMessage
{
    public string? Id { get; set; }
    public string? Method { get; set; }
    public string? Event { get; set; }
    public bool? Ok { get; set; }
    public string? Error { get; set; }
    public JsonElement? Params { get; set; }
    public JsonElement? Result { get; set; }
}

public sealed record HelloRequest(string ClientName, int Pid, string Version);

public sealed record HelloResponse(
    string ServerName,
    int Pid,
    string Version,
    string LibraryPath);

public sealed record LibraryClientInfo(
    int ConnectionId,
    string ClientName,
    int Pid,
    DateTimeOffset ConnectedAtUtc);

public sealed record LibraryStatus(
    string LibraryPath,
    string LibraryId,
    int CharacterCount,
    int DaemonPid,
    string DaemonVersion,
    DateTimeOffset StartedAtUtc,
    int PendingWrites,
    List<LibraryClientInfo> Clients);

public sealed record CharacterSummary(
    string Id,
    string Name,
    int StartSize = 0,
    DateTimeOffset ImportedAtUtc = default);

public sealed record CharacterImageInfo(string Slot, string MimeType, int Bytes);

public sealed record CharacterLayerOffset(string Layer, int X, int Y);

public sealed record CharacterRunSettings(
    double Width,
    double Height,
    int? X,
    int? Y,
    bool Flipped,
    bool Topmost,
    bool Locked);

public sealed record CharacterRecord(
    string Id,
    string Name,
    int StartSize,
    string SourceFile,
    List<CharacterImageInfo> Images,
    List<string> PartOrder,
    List<CharacterLayerOffset> LayerOffsets,
    CharacterRunSettings Settings);

public sealed record ImportCvclkRequest(string Path);

public sealed record ImportedCharacterInfo(string Id, string Name);

public sealed record CharacterImportSkip(string? SourceId, string? Name, string Reason);

public sealed record CharacterImportResult(
    List<ImportedCharacterInfo> Imported,
    List<CharacterImportSkip> Skipped);

public sealed record GetCharacterRequest(string CharacterId);

public sealed record GetCharacterImageRequest(string CharacterId, string Slot);

public sealed record CharacterImageResponse(
    string CharacterId,
    string Slot,
    string MimeType,
    string Base64);

public sealed record SaveRunSettingsRequest(string CharacterId, CharacterRunSettings Settings);

public sealed record CharacterIdRequest(string CharacterId, string? LibraryPath = null);

public sealed record LibraryPathRequest(string? LibraryPath = null);

public sealed record CharacterState(
    bool Visible,
    bool ClickThrough,
    bool Locked,
    bool Topmost,
    bool Flipped,
    bool LibraryConnected,
    int Pid,
    string Version,
    string? LibraryPath,
    string? CharacterId = null,
    string? CharacterName = null);

public sealed record CharacterHello(string Token, int Pid, string Version);

public sealed record CharacterCommand(string Name, bool? Enabled, string? LanguageTag = null);

public sealed record SetClickThroughRequest(bool Enabled, string? LibraryPath = null);

public sealed record ProcessInfo(
    string Name,
    bool Running,
    int? Pid,
    string? Version,
    string? Detail);

public sealed record CharacterRuntimeStatus(
    string? CharacterId,
    string? CharacterName,
    ProcessInfo Process,
    CharacterState? State);

public sealed record LibraryTopologyStatus(
    string LibraryPath,
    string DisplayName,
    bool ManagementWindowOpen,
    ProcessInfo LibraryDaemon,
    List<CharacterRuntimeStatus> CharacterRuntimes);

public sealed record GlobalStatus(
    int Pid,
    string Version,
    string RuntimeRoot,
    string LibraryPath,
    bool ManagementWindowOpen,
    ProcessInfo LibraryDaemon,
    ProcessInfo CharacterRuntime,
    CharacterState? CharacterState,
    List<CharacterRuntimeStatus>? CharacterRuntimes = null,
    List<LibraryTopologyStatus>? Libraries = null);

public sealed record CommandResult(bool Accepted, string? Message = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IpcMessage))]
[JsonSerializable(typeof(HelloRequest))]
[JsonSerializable(typeof(HelloResponse))]
[JsonSerializable(typeof(LibraryClientInfo))]
[JsonSerializable(typeof(List<LibraryClientInfo>))]
[JsonSerializable(typeof(LibraryStatus))]
[JsonSerializable(typeof(CharacterSummary))]
[JsonSerializable(typeof(List<CharacterSummary>))]
[JsonSerializable(typeof(CharacterImageInfo))]
[JsonSerializable(typeof(List<CharacterImageInfo>))]
[JsonSerializable(typeof(CharacterLayerOffset))]
[JsonSerializable(typeof(List<CharacterLayerOffset>))]
[JsonSerializable(typeof(CharacterRunSettings))]
[JsonSerializable(typeof(CharacterRecord))]
[JsonSerializable(typeof(ImportCvclkRequest))]
[JsonSerializable(typeof(ImportedCharacterInfo))]
[JsonSerializable(typeof(List<ImportedCharacterInfo>))]
[JsonSerializable(typeof(CharacterImportSkip))]
[JsonSerializable(typeof(List<CharacterImportSkip>))]
[JsonSerializable(typeof(CharacterImportResult))]
[JsonSerializable(typeof(GetCharacterRequest))]
[JsonSerializable(typeof(GetCharacterImageRequest))]
[JsonSerializable(typeof(CharacterImageResponse))]
[JsonSerializable(typeof(SaveRunSettingsRequest))]
[JsonSerializable(typeof(CharacterIdRequest))]
[JsonSerializable(typeof(LibraryPathRequest))]
[JsonSerializable(typeof(CharacterState))]
[JsonSerializable(typeof(CharacterRuntimeStatus))]
[JsonSerializable(typeof(List<CharacterRuntimeStatus>))]
[JsonSerializable(typeof(LibraryTopologyStatus))]
[JsonSerializable(typeof(List<LibraryTopologyStatus>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(CharacterHello))]
[JsonSerializable(typeof(CharacterCommand))]
[JsonSerializable(typeof(SetClickThroughRequest))]
[JsonSerializable(typeof(ProcessInfo))]
[JsonSerializable(typeof(GlobalStatus))]
[JsonSerializable(typeof(CommandResult))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;

public static class ProtocolJson
{
    public static JsonElement ToElement<T>(T value) =>
        JsonSerializer.SerializeToElement(value, TypeInfo<T>());

    public static T FromElement<T>(JsonElement element) =>
        JsonSerializer.Deserialize(element, TypeInfo<T>())
        ?? throw new InvalidDataException($"Could not deserialize IPC payload as {typeof(T).Name}.");

    public static IpcMessage Parse(string line) =>
        JsonSerializer.Deserialize(line, ProtocolJsonContext.Default.IpcMessage)
        ?? throw new InvalidDataException("IPC line did not contain a message.");

    public static string Serialize(IpcMessage message) =>
        JsonSerializer.Serialize(message, ProtocolJsonContext.Default.IpcMessage);

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        ProtocolJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new InvalidOperationException($"No JSON contract is registered for {typeof(T).Name}.");
}