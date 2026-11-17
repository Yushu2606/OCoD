using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using OCoD.Formats;
using OCoD.Protocol;

namespace OCoD.LibraryDaemon;

/// <summary>Character records and images inside one library. Only the library daemon calls this.</summary>
internal sealed class CharacterLibrary
{
    private const int SchemaVersion = 1;
    private const int DefaultCharacterSize = 160;

    private readonly string _libraryPath;
    private readonly string _charactersDirectory;
    private readonly string _indexPath;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pendingWrites;

    public CharacterLibrary(string libraryPath, Action<string> log)
    {
        _libraryPath = libraryPath;
        _charactersDirectory = Path.Combine(libraryPath, "characters");
        _indexPath = Path.Combine(_charactersDirectory, "index.json");
        _log = log;
    }

    public int PendingWrites => Volatile.Read(ref _pendingWrites);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_charactersDirectory);

            if (!File.Exists(_indexPath))
            {
                await WriteJsonAsync(
                    _indexPath,
                    new StoredIndex(SchemaVersion, []),
                    CharacterLibraryJsonContext.Default.StoredIndex,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterImportResult> ImportCvclkAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var fullPath = Path.GetFullPath(packagePath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _pendingWrites);

        try
        {
            _log($"import package '{fullPath}'");
            var container = await CvclkContainer.ReadFileAsync(fullPath, cancellationToken)
                .ConfigureAwait(false);
            var import = await CvclkImporter.ImportAsync(
                container.PayloadJson,
                new CvclkImporterOptions(),
                cancellationToken).ConfigureAwait(false);

            var index = await ReadIndexAsync(cancellationToken).ConfigureAwait(false);
            var imported = new List<ImportedCharacterInfo>();
            var skipped = import.Skipped
                .Select(skip => new CharacterImportSkip(skip.SourceId, skip.Name, skip.Reason))
                .ToList();
            var sourceFile = Path.GetFileName(fullPath);

            foreach (var character in import.Characters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = Guid.NewGuid().ToString("N");
                var characterDirectory = Path.Combine(_charactersDirectory, id);

                try
                {
                    Directory.CreateDirectory(characterDirectory);
                    var imagesDirectory = Path.Combine(characterDirectory, "images");
                    Directory.CreateDirectory(imagesDirectory);

                    var storedImages = new List<StoredImage>();
                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var image in character.Images.Values)
                    {
                        var fileName = BuildImageFileName(image.Slot, image.Extension, usedNames);
                        var imagePath = Path.Combine(imagesDirectory, fileName);
                        await File.WriteAllBytesAsync(imagePath, image.Bytes, cancellationToken)
                            .ConfigureAwait(false);

                        storedImages.Add(new StoredImage(
                            image.Slot,
                            image.MimeType,
                            Path.Combine("images", fileName),
                            image.Bytes.Length));
                    }

                    var startSize = character.StartSize is >= 60 and <= 600
                        ? character.StartSize
                        : 0;
                    var defaultSize = startSize > 0 ? startSize : DefaultCharacterSize;
                    var importedAt = DateTimeOffset.UtcNow;

                    var stored = new StoredCharacter(
                        SchemaVersion,
                        id,
                        character.Name,
                        character.SourceId,
                        startSize,
                        importedAt.ToString("O"),
                        sourceFile,
                        storedImages,
                        character.PartOrder,
                        character.LayerOffsets
                            .Select(pair => new StoredLayerOffset(pair.Key, pair.Value.X, pair.Value.Y))
                            .ToList(),
                        character.RawCharacterJson);

                    await WriteJsonAsync(
                        Path.Combine(characterDirectory, "character.json"),
                        stored,
                        CharacterLibraryJsonContext.Default.StoredCharacter,
                        cancellationToken).ConfigureAwait(false);

                    await WriteJsonAsync(
                        Path.Combine(characterDirectory, "settings.json"),
                        new StoredSettings(
                            defaultSize,
                            defaultSize,
                            null,
                            null,
                            Flipped: false,
                            Topmost: false,
                            Locked: false),
                        CharacterLibraryJsonContext.Default.StoredSettings,
                        cancellationToken).ConfigureAwait(false);

                    index.Characters.Add(new StoredIndexEntry(
                        id,
                        character.Name,
                        startSize,
                        importedAt.ToString("O"),
                        sourceFile));

                    imported.Add(new ImportedCharacterInfo(id, character.Name));
                    _log($"imported character '{character.Name}' as {id}");
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or JsonException)
                {
                    TryDeleteDirectory(characterDirectory);
                    skipped.Add(new CharacterImportSkip(
                        character.SourceId,
                        character.Name,
                        exception.Message));
                    _log($"failed to import character '{character.Name}': {exception.Message}");
                }
            }

            await WriteJsonAsync(
                _indexPath,
                index,
                CharacterLibraryJsonContext.Default.StoredIndex,
                cancellationToken).ConfigureAwait(false);

            return new CharacterImportResult(imported, skipped);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingWrites);
            _gate.Release();
        }
    }

    public async Task<int> GetCharacterCountAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await ReadIndexAsync(cancellationToken).ConfigureAwait(false);
            return index.Characters.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<CharacterSummary>> ListCharactersAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await ReadIndexAsync(cancellationToken).ConfigureAwait(false);

            return index.Characters
                .Select(entry => new CharacterSummary(
                    entry.Id,
                    entry.Name,
                    entry.StartSize,
                    ParseDate(entry.ImportedAtUtc)))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> GetCharacterAsync(
        string characterId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await ReadCharacterAsync(characterId, cancellationToken).ConfigureAwait(false);
            var settings = await ReadSettingsAsync(characterId, cancellationToken).ConfigureAwait(false);

            return new CharacterRecord(
                stored.Id,
                stored.Name,
                stored.StartSize,
                stored.SourceFile,
                stored.Images
                    .Select(image => new CharacterImageInfo(image.Slot, image.MimeType, image.Bytes))
                    .ToList(),
                stored.PartOrder,
                stored.LayerOffsets
                    .Select(offset => new CharacterLayerOffset(offset.Layer, offset.X, offset.Y))
                    .ToList(),
                new CharacterRunSettings(
                    settings.Width,
                    settings.Height,
                    settings.X,
                    settings.Y,
                    settings.Flipped,
                    settings.Topmost,
                    settings.Locked));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterImageResponse?> GetImageAsync(
        string characterId,
        string slot,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await ReadCharacterAsync(characterId, cancellationToken).ConfigureAwait(false);
            var image = stored.Images.FirstOrDefault(item =>
                string.Equals(item.Slot, slot, StringComparison.Ordinal));

            if (image is null)
            {
                return null;
            }

            var path = ResolveCharacterFile(characterId, image.File);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

            return new CharacterImageResponse(
                characterId,
                image.Slot,
                image.MimeType,
                Convert.ToBase64String(bytes));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveSettingsAsync(
        string characterId,
        CharacterRunSettings settings,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _pendingWrites);

        try
        {
            _ = await ReadCharacterAsync(characterId, cancellationToken).ConfigureAwait(false);

            var stored = new StoredSettings(
                Math.Clamp(settings.Width, 60, 1200),
                Math.Clamp(settings.Height, 60, 1200),
                settings.X is { } x ? Math.Clamp(x, -1_000_000, 1_000_000) : null,
                settings.Y is { } y ? Math.Clamp(y, -1_000_000, 1_000_000) : null,
                settings.Flipped,
                settings.Topmost,
                settings.Locked);

            await WriteJsonAsync(
                GetCharacterPath(characterId, "settings.json"),
                stored,
                CharacterLibraryJsonContext.Default.StoredSettings,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingWrites);
            _gate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _gate.Release();
    }

    private async Task<StoredCharacter> ReadCharacterAsync(
        string characterId,
        CancellationToken cancellationToken)
    {
        var path = GetCharacterPath(characterId, "character.json");

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, CharacterLibraryJsonContext.Default.StoredCharacter)
                ?? throw new InvalidDataException($"Character record '{path}' is empty.");
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new FileNotFoundException("Character was not found.", path, exception);
        }
    }

    private async Task<StoredSettings> ReadSettingsAsync(
        string characterId,
        CancellationToken cancellationToken)
    {
        var path = GetCharacterPath(characterId, "settings.json");

        if (!File.Exists(path))
        {
            return new StoredSettings(
                DefaultCharacterSize,
                DefaultCharacterSize,
                null,
                null,
                false,
                false,
                false);
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, CharacterLibraryJsonContext.Default.StoredSettings)
            ?? new StoredSettings(
                DefaultCharacterSize,
                DefaultCharacterSize,
                null,
                null,
                false,
                false,
                false);
    }

    private async Task<StoredIndex> ReadIndexAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_indexPath))
        {
            return new StoredIndex(SchemaVersion, []);
        }

        var json = await File.ReadAllTextAsync(_indexPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, CharacterLibraryJsonContext.Default.StoredIndex)
            ?? new StoredIndex(SchemaVersion, []);
    }

    private string GetCharacterPath(string characterId, string fileName)
    {
        if (!IsValidCharacterId(characterId))
        {
            throw new FileNotFoundException("Character was not found.");
        }

        return Path.Combine(_charactersDirectory, characterId, fileName);
    }

    private string ResolveCharacterFile(string characterId, string relativePath)
    {
        if (!IsValidCharacterId(characterId))
        {
            throw new FileNotFoundException("Character was not found.");
        }

        var characterDirectory = Path.GetFullPath(Path.Combine(_charactersDirectory, characterId));
        var fullPath = Path.GetFullPath(Path.Combine(characterDirectory, relativePath));
        var prefix = characterDirectory + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(prefix, StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            throw new FileNotFoundException("Character image was not found.");
        }

        return fullPath;
    }

    private static bool IsValidCharacterId(string characterId) =>
        characterId.Length == 32 && characterId.All(Uri.IsHexDigit);

    private static string BuildImageFileName(
        string slot,
        string extension,
        HashSet<string> usedNames)
    {
        var sanitized = new string(slot
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            .ToArray());

        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "image";
        }

        var candidate = $"{sanitized}.{extension}";
        var suffix = 2;

        while (!usedNames.Add(candidate))
        {
            candidate = $"{sanitized}-{suffix++}.{extension}";
        }

        return candidate;
    }

    private async Task WriteJsonAsync<T>(
        string path,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        var json = JsonSerializer.Serialize(value, typeInfo);

        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out var date) ? date : default;

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record StoredIndex(int SchemaVersion, List<StoredIndexEntry> Characters);

internal sealed record StoredIndexEntry(
    string Id,
    string Name,
    int StartSize,
    string ImportedAtUtc,
    string SourceFile);

internal sealed record StoredCharacter(
    int SchemaVersion,
    string Id,
    string Name,
    string SourceId,
    int StartSize,
    string ImportedAtUtc,
    string SourceFile,
    List<StoredImage> Images,
    List<string> PartOrder,
    List<StoredLayerOffset> LayerOffsets,
    string RawCharacterJson);

internal sealed record StoredImage(
    string Slot,
    string MimeType,
    string File,
    int Bytes);

internal sealed record StoredLayerOffset(string Layer, int X, int Y);

internal sealed record StoredSettings(
    double Width,
    double Height,
    int? X,
    int? Y,
    bool Flipped,
    bool Topmost,
    bool Locked);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(StoredIndex))]
[JsonSerializable(typeof(StoredCharacter))]
[JsonSerializable(typeof(StoredSettings))]
internal sealed partial class CharacterLibraryJsonContext : JsonSerializerContext;