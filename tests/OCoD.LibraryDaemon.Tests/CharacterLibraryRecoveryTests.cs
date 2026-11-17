using OCoD.LibraryDaemon;

namespace OCoD.LibraryDaemon.Tests;

public sealed class CharacterLibraryRecoveryTests
{
    [Fact]
    public async Task Initialize_reindexes_a_complete_orphan_and_restores_settings()
    {
        using var temporary = new TemporaryLibrary();
        await CreateLibraryAsync(temporary.Path);

        const string id = "0123456789abcdef0123456789abcdef";
        var characterDirectory = await CreateCharacterAsync(
            temporary.Path,
            id,
            "Recovered character",
            startSize: 120);

        var library = new CharacterLibrary(temporary.Path, _ => { });
        await library.InitializeAsync();

        var summary = Assert.Single(await library.ListCharactersAsync());
        Assert.Equal(id, summary.Id);
        Assert.Equal("Recovered character", summary.Name);
        Assert.Equal(120, summary.StartSize);

        var record = await library.GetCharacterAsync(id);
        Assert.NotNull(record);
        Assert.Equal(120, record.Settings.Width);
        Assert.Equal(120, record.Settings.Height);
        Assert.True(File.Exists(Path.Combine(characterDirectory, "settings.json")));

        var index = await ReadIndexAsync(temporary.Path);
        Assert.Contains(id, index);
    }

    [Fact]
    public async Task Initialize_removes_an_incomplete_orphan()
    {
        using var temporary = new TemporaryLibrary();
        await CreateLibraryAsync(temporary.Path);

        const string id = "11111111111111111111111111111111";
        var characterDirectory = Path.Combine(temporary.Path, "characters", id);
        Directory.CreateDirectory(characterDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(characterDirectory, "character.json"),
            "{ not valid json");

        var library = new CharacterLibrary(temporary.Path, _ => { });
        await library.InitializeAsync();

        Assert.False(Directory.Exists(characterDirectory));
        Assert.Empty(await library.ListCharactersAsync());
    }

    [Fact]
    public async Task Initialize_drops_index_entries_without_directories()
    {
        using var temporary = new TemporaryLibrary();
        await CreateLibraryAsync(temporary.Path);

        const string id = "22222222222222222222222222222222";
        await WriteIndexAsync(
            temporary.Path,
            $$"""
              {
                "schemaVersion": 1,
                "characters": [
                  {
                    "id": "{{id}}",
                    "name": "Missing character",
                    "startSize": 160,
                    "importedAtUtc": "2026-01-01T00:00:00.0000000+00:00",
                    "sourceFile": "missing.cvclk"
                  }
                ]
              }
              """);

        var library = new CharacterLibrary(temporary.Path, _ => { });
        await library.InitializeAsync();

        Assert.Empty(await library.ListCharactersAsync());
        Assert.DoesNotContain(id, await ReadIndexAsync(temporary.Path));
    }

    [Fact]
    public async Task Initialize_rebuilds_a_corrupt_index_from_character_directories()
    {
        using var temporary = new TemporaryLibrary();
        await CreateLibraryAsync(temporary.Path);

        const string id = "33333333333333333333333333333333";
        await CreateCharacterAsync(temporary.Path, id, "Corrupt index survivor", startSize: 160);
        await File.WriteAllTextAsync(
            Path.Combine(temporary.Path, "characters", "index.json"),
            "{ corrupt");

        var library = new CharacterLibrary(temporary.Path, _ => { });
        await library.InitializeAsync();

        var summary = Assert.Single(await library.ListCharactersAsync());
        Assert.Equal(id, summary.Id);
        Assert.Contains(id, await ReadIndexAsync(temporary.Path));
    }

    [Fact]
    public async Task A_failed_index_commit_rolls_back_new_character_directories()
    {
        using var temporary = new TemporaryLibrary();
        await CreateLibraryAsync(temporary.Path);

        var indexDirectory = Path.Combine(temporary.Path, "characters", "index.json");
        File.Delete(indexDirectory);
        Directory.CreateDirectory(indexDirectory);

        var library = new CharacterLibrary(temporary.Path, _ => { });
        var sample = Path.Combine(AppContext.BaseDirectory, "samples", "plain-minimal.cvclk");

        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => library.ImportCvclkAsync(sample));

        // Windows reports the directory destination as access denied; Unix reports an I/O error.
        Assert.True(
            exception is IOException or UnauthorizedAccessException,
            $"Unexpected commit failure: {exception}");

        var characterDirectories = Directory.EnumerateDirectories(
                Path.Combine(temporary.Path, "characters"))
            .Where(directory => IsCharacterId(Path.GetFileName(directory)))
            .ToList();

        Assert.Empty(characterDirectories);
        Assert.False(File.Exists(indexDirectory + ".tmp"));
        Directory.Delete(indexDirectory);
    }

    private static async Task CreateLibraryAsync(string libraryPath)
    {
        Directory.CreateDirectory(Path.Combine(libraryPath, "characters"));
        await WriteIndexAsync(libraryPath, """{"schemaVersion":1,"characters":[]}""");
    }

    private static async Task<string> CreateCharacterAsync(
        string libraryPath,
        string id,
        string name,
        int startSize)
    {
        var characterDirectory = Path.Combine(libraryPath, "characters", id);
        var imagesDirectory = Path.Combine(characterDirectory, "images");
        Directory.CreateDirectory(imagesDirectory);
        await File.WriteAllBytesAsync(Path.Combine(imagesDirectory, "body.png"), [1, 2, 3]);

        var json = $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "name": "{{name}}",
              "sourceId": "sample-{{id}}",
              "startSize": {{startSize}},
              "importedAtUtc": "2026-01-01T00:00:00.0000000+00:00",
              "sourceFile": "sample.cvclk",
              "images": [
                {
                  "slot": "body",
                  "mimeType": "image/png",
                  "file": "images/body.png",
                  "bytes": 3
                }
              ],
              "partOrder": [],
              "layerOffsets": [],
              "rawCharacterJson": "{}"
            }
            """;

        await File.WriteAllTextAsync(Path.Combine(characterDirectory, "character.json"), json);
        return characterDirectory;
    }

    private static async Task WriteIndexAsync(string libraryPath, string json)
    {
        var charactersDirectory = Path.Combine(libraryPath, "characters");
        Directory.CreateDirectory(charactersDirectory);
        await File.WriteAllTextAsync(Path.Combine(charactersDirectory, "index.json"), json);
    }

    private static async Task<string> ReadIndexAsync(string libraryPath)
    {
        var path = Path.Combine(libraryPath, "characters", "index.json");
        return await File.ReadAllTextAsync(path);
    }

    private static bool IsCharacterId(string value) =>
        value.Length == 32 && value.All(Uri.IsHexDigit);

    private sealed class TemporaryLibrary : IDisposable
    {
        public TemporaryLibrary()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ocod-library-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}