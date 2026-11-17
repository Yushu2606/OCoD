using System.Text.Json;
using System.Text.Json.Serialization;

using OCoD.Protocol;

namespace OCoD.LibraryDaemon;

/// <summary>
/// The only writer of a character library. Other components must go through the library daemon.
/// </summary>
internal sealed class LibraryStore
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(LibraryJsonContext.Default.Options)
    {
        WriteIndented = true,
    };

    private readonly string _libraryPath;
    private readonly string _metadataPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _pendingWrites;

    public LibraryStore(string libraryPath)
    {
        _libraryPath = libraryPath;
        _metadataPath = Path.Combine(
            OcodPaths.LibraryMetadataDirectory(libraryPath),
            "library.json");
    }

    public string LibraryId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public int CharacterCount => 0;

    public int PendingWrites => Volatile.Read(ref _pendingWrites);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(OcodPaths.LibraryMetadataDirectory(_libraryPath));
        Directory.CreateDirectory(
            Path.GetDirectoryName(OcodPaths.LibraryLogFile(_libraryPath))!);

        if (File.Exists(_metadataPath))
        {
            var json = await File.ReadAllTextAsync(_metadataPath, cancellationToken)
                .ConfigureAwait(false);
            var metadata = JsonSerializer.Deserialize(json, LibraryJsonContext.Default.LibraryMetadata)
                ?? throw new InvalidDataException($"Library metadata '{_metadataPath}' is empty.");

            if (metadata.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported library schema version {metadata.SchemaVersion} in '{_metadataPath}'.");
            }

            LibraryId = metadata.LibraryId;
            CreatedAtUtc = DateTimeOffset.Parse(
                metadata.CreatedAtUtc,
                System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        LibraryId = Guid.NewGuid().ToString("N");
        CreatedAtUtc = DateTimeOffset.UtcNow;
        await WriteMetadataAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        _writeLock.Release();
    }

    private async Task WriteMetadataAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _pendingWrites);

        try
        {
            var metadata = new LibraryMetadata(
                CurrentSchemaVersion,
                LibraryId,
                CreatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

            var json = JsonSerializer.Serialize(
                metadata,
                LibraryJsonContext.Default.LibraryMetadata);
            var temporaryPath = _metadataPath + ".tmp";

            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, _metadataPath, overwrite: true);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingWrites);
            _writeLock.Release();
        }
    }
}

internal sealed record LibraryMetadata(
    int SchemaVersion,
    string LibraryId,
    string CreatedAtUtc);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LibraryMetadata))]
internal sealed partial class LibraryJsonContext : JsonSerializerContext;