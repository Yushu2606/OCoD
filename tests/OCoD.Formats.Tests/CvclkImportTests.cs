using OCoD.Formats;

namespace OCoD.Formats.Tests;

public sealed class CvclkImportTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task Plain_minimal_imports_name_and_base_images()
    {
        var (payload, import) = await ImportAsync("plain-minimal.cvclk");

        Assert.False(payload.IsPlainJson);
        Assert.Equal("bug-clicker", System.Text.Json.Nodes.JsonNode.Parse(payload.PayloadJson)!["format"]!.GetValue<string>());
        Assert.Single(import.Characters);

        var character = import.Characters[0];
        Assert.Equal("净室样本·小橙", character.Name);
        Assert.True(character.Images.ContainsKey("body"));
        Assert.True(character.Images.ContainsKey("head1"));
        Assert.True(character.Images.ContainsKey("head2"));
        Assert.Equal("image/png", character.Images["body"].MimeType);
        Assert.Equal("png", character.Images["body"].Extension);
    }

    [Fact]
    public async Task Plain_multi_imports_all_characters_and_falls_back_to_id()
    {
        var (_, import) = await ImportAsync("plain-multi.cvclk");

        Assert.Equal(3, import.Characters.Count);
        Assert.Contains(import.Characters, character => character.Name == "Cleanroom Sample Lime");
        Assert.Contains(import.Characters, character => character.Name == "クリーンルーム見本");
        Assert.Contains(import.Characters, character => character.Name == "unnamed-sample-orange");

        var japanese = import.Characters.Single(character => character.Name == "クリーンルーム見本");
        Assert.Equal(japanese.Images["head1"].Bytes, japanese.Images["head2"].Bytes);
    }

    [Fact]
    public async Task Plain_full_preserves_layer_images_and_offsets()
    {
        var (_, import) = await ImportAsync("plain-full.cvclk");

        var character = Assert.Single(import.Characters);
        Assert.Equal("净室样本·彩绘", character.Name);
        Assert.Equal(120, character.StartSize);
        Assert.True(character.Images.ContainsKey("body"));
        Assert.True(character.Images.ContainsKey("head1"));
        Assert.True(character.Images.ContainsKey("head5"));
        Assert.True(character.Images.ContainsKey("side1"));
        Assert.True(character.Images.ContainsKey("ex4p"));
        Assert.True(character.LayerOffsets.ContainsKey("back"));
        Assert.Equal(new LayerOffset(12, -8), character.LayerOffsets["back"]);
        Assert.Contains("L:back", character.PartOrder);
    }

    [Fact]
    public async Task Unknown_fields_are_kept_in_the_raw_character_json()
    {
        var (_, import) = await ImportAsync("unknown-fields.cvclk");

        var character = Assert.Single(import.Characters);
        Assert.Contains("x_future_field", character.RawCharacterJson);
        Assert.Contains("x_future_object", character.RawCharacterJson);
        Assert.Contains("xFutureLayer", character.RawCharacterJson);
    }

    [Fact]
    public async Task Animated_gif_is_accepted_by_byte_signature()
    {
        var (_, import) = await ImportAsync("animated-gif.cvclk");

        var character = Assert.Single(import.Characters);
        Assert.Equal("image/gif", character.Images["head3"].MimeType);
        Assert.Equal("gif", character.Images["head3"].Extension);
    }

    [Fact]
    public async Task Tampered_ciphertext_is_rejected()
    {
        var path = Path.Combine(RepositoryRoot, "samples", "cvclk", "plain-minimal.cvclk");
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0x01;

        var exception = Assert.Throws<CvclkFormatException>(() => CvclkContainer.Read(bytes));
        Assert.Equal(CvclkError.Corrupt, exception.Error);
    }

    [Fact]
    public async Task Tampered_header_is_rejected()
    {
        var path = Path.Combine(RepositoryRoot, "samples", "cvclk", "plain-minimal.cvclk");
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[12] ^= 0x01;

        var exception = Assert.Throws<CvclkFormatException>(() => CvclkContainer.Read(bytes));
        Assert.Equal(CvclkError.Corrupt, exception.Error);
    }

    private static async Task<(CvclkReadResult Payload, CvclkImportResult Import)> ImportAsync(
        string fileName)
    {
        var path = Path.Combine(RepositoryRoot, "samples", "cvclk", fileName);
        var payload = await CvclkContainer.ReadFileAsync(path);
        var import = await CvclkImporter.ImportAsync(
            payload.PayloadJson,
            new CvclkImporterOptions
            {
                HttpClient = null,
            });

        return (payload, import);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OCoD.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}