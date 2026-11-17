using System.Net;
using System.Text.Json.Nodes;

namespace OCoD.Formats;

public sealed record ImportedImage(
    string Slot,
    string MimeType,
    string Extension,
    byte[] Bytes);

public sealed record LayerOffset(int X, int Y);

public sealed class ImportedCharacter
{
    public required string SourceId { get; init; }
    public required string Name { get; init; }
    public required int StartSize { get; init; }
    public required Dictionary<string, ImportedImage> Images { get; init; }
    public required List<string> PartOrder { get; init; }
    public required Dictionary<string, LayerOffset> LayerOffsets { get; init; }
    public required string RawCharacterJson { get; init; }
}

public sealed record SkippedCharacter(string? SourceId, string? Name, string Reason);

public sealed class CvclkImportResult
{
    public List<ImportedCharacter> Characters { get; } = [];
    public List<SkippedCharacter> Skipped { get; } = [];
}

public sealed class CvclkImporterOptions
{
    public string UnnamedCharacter { get; init; } = "Unnamed character";

    /// <summary>Used for the legacy imageUrls fallback; set to null to disable network access.</summary>
    public HttpClient? HttpClient { get; init; } = SharedHttpClient;

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };
}

/// <summary>Parses a normal bug-clicker v1 payload into the pieces the runtime needs.</summary>
public static class CvclkImporter
{
    private const int MaxImageBytes = 8_388_608;

    private static readonly string[] KnownImageSlots =
    [
        "body", "head1", "head2", "head3", "head4", "head5", "body2",
        "back1", "back2", "side1", "side2",
        "ex1", "ex1p", "ex2", "ex2p", "ex3", "ex3p", "ex4", "ex4p",
    ];

    private static readonly string[] LayerNames = ["back", "side", "ex1", "ex2", "ex3", "ex4"];

    public static async Task<CvclkImportResult> ImportAsync(
        string payloadJson,
        CvclkImporterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CvclkImporterOptions();

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payloadJson);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException)
        {
            throw new CvclkFormatException(
                CvclkError.Corrupt,
                "The character payload is not valid JSON.",
                exception);
        }

        if (root is not JsonObject rootObject)
        {
            throw new CvclkFormatException(CvclkError.Corrupt, "The character payload must be an object.");
        }

        if (rootObject["format"]?.GetValue<string>() != "bug-clicker")
        {
            throw new CvclkFormatException(
                CvclkError.NotCvclk,
                "The payload format is not 'bug-clicker'.");
        }

        if (rootObject["characters"] is not JsonArray characters || characters.Count == 0)
        {
            throw new CvclkFormatException(
                CvclkError.Corrupt,
                "The payload must contain a non-empty characters array.");
        }

        if (characters.Any(character => character is JsonObject entry && HasProtectionMarkers(entry)))
        {
            throw new CvclkFormatException(
                CvclkError.Protected,
                "Protected cvclk packages are not supported by this importer yet.");
        }

        var result = new CvclkImportResult();

        foreach (var node in characters)
        {
            if (node is not JsonObject character)
            {
                result.Skipped.Add(new SkippedCharacter(null, null, "Character entry is not an object."));
                continue;
            }

            try
            {
                var imported = await ImportCharacterAsync(
                    character,
                    options,
                    cancellationToken).ConfigureAwait(false);

                if (imported is null)
                {
                    result.Skipped.Add(new SkippedCharacter(
                        GetString(character["id"]),
                        GetString(character["name"]),
                        "The character has no valid body or head1 image."));
                    continue;
                }

                result.Characters.Add(imported);
            }
            catch (Exception exception)
            {
                result.Skipped.Add(new SkippedCharacter(
                    GetString(character["id"]),
                    GetString(character["name"]),
                    exception.Message));
            }
        }

        return result;
    }

    private static async Task<ImportedCharacter?> ImportCharacterAsync(
        JsonObject character,
        CvclkImporterOptions options,
        CancellationToken cancellationToken)
    {
        var sourceId = GetString(character["id"]) ?? string.Empty;

        var images = new Dictionary<string, ImportedImage>(StringComparer.Ordinal);
        await ReadImagesAsync(character, images, options, cancellationToken).ConfigureAwait(false);

        if (!images.ContainsKey("head2") && images.TryGetValue("head1", out var head1))
        {
            images["head2"] = head1 with { Slot = "head2" };
        }

        if (!images.ContainsKey("head1") && images.TryGetValue("body", out var body))
        {
            images["head1"] = body with { Slot = "head1" };
        }

        if (!images.ContainsKey("body") && images.TryGetValue("head1", out var head))
        {
            images["body"] = head with { Slot = "body" };
        }

        if (!images.ContainsKey("back2") && images.TryGetValue("back1", out var back1))
        {
            images["back2"] = back1 with { Slot = "back2" };
        }

        if (!images.ContainsKey("side2") && images.TryGetValue("side1", out var side1))
        {
            images["side2"] = side1 with { Slot = "side2" };
        }

        if (!images.ContainsKey("body") && !images.ContainsKey("head1"))
        {
            return null;
        }

        var nameValue = (GetString(character["name"]) ?? string.Empty).Trim();
        var name = !string.IsNullOrWhiteSpace(nameValue)
            ? nameValue
            : !string.IsNullOrWhiteSpace(sourceId)
                ? sourceId
                : options.UnnamedCharacter;

        var startSize = GetInt(character["startSize"]) ?? 0;
        startSize = startSize is >= 60 and <= 600 ? startSize : 0;

        var partOrder = new List<string>();
        if (character["partOrder"] is JsonArray partOrderArray)
        {
            foreach (var item in partOrderArray)
            {
                if (item is JsonValue value && value.TryGetValue<string>(out var entry))
                {
                    partOrder.Add(entry);
                }
            }
        }

        var layerOffsets = new Dictionary<string, LayerOffset>(StringComparer.Ordinal);
        if (character["layers"] is JsonObject layers)
        {
            foreach (var layer in LayerNames)
            {
                var x = GetInt(layers[layer + "X"]) ?? 0;
                var y = GetInt(layers[layer + "Y"]) ?? 0;
                x = Math.Clamp(x, -400, 400);
                y = Math.Clamp(y, -400, 400);

                if (x != 0 || y != 0)
                {
                    layerOffsets[layer] = new LayerOffset(x, y);
                }
            }
        }

        return new ImportedCharacter
        {
            SourceId = sourceId,
            Name = name,
            StartSize = startSize,
            Images = images,
            PartOrder = partOrder,
            LayerOffsets = layerOffsets,
            RawCharacterJson = character.ToJsonString(),
        };
    }

    private static async Task ReadImagesAsync(
        JsonObject character,
        Dictionary<string, ImportedImage> images,
        CvclkImporterOptions options,
        CancellationToken cancellationToken)
    {
        var imagesNode = character["images"] as JsonObject;
        var imageUrls = character["imageUrls"] as JsonObject;

        foreach (var slot in KnownImageSlots)
        {
            if (imagesNode?[slot] is JsonValue value
                && value.TryGetValue<string>(out var dataUrl)
                && TryDecodeImage(slot, dataUrl, out var image))
            {
                images[slot] = image!;
                continue;
            }

            if (options.HttpClient is not null
                && imageUrls?[slot] is JsonValue urlValue
                && urlValue.TryGetValue<string>(out var url)
                && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var bytes = await DownloadImageAsync(options.HttpClient, uri, cancellationToken)
                    .ConfigureAwait(false);
                if (bytes is not null && TryDecodeImageBytes(slot, bytes, out var downloadedImage))
                {
                    images[slot] = downloadedImage!;
                }
            }
        }

        // Preserve unknown image slots for future re-export/display extensions.
        if (imagesNode is not null)
        {
            foreach (var (slot, node) in imagesNode)
            {
                if (images.ContainsKey(slot) || KnownImageSlots.Contains(slot, StringComparer.Ordinal))
                {
                    continue;
                }

                if (node is JsonValue value
                    && value.TryGetValue<string>(out var dataUrl)
                    && TryDecodeImage(slot, dataUrl, out var image))
                {
                    images[slot] = image!;
                }
            }
        }
    }

    private static async Task<byte[]?> DownloadImageAsync(
        HttpClient client,
        Uri uri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is > MaxImageBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;

            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > MaxImageBytes)
                {
                    return null;
                }

                memory.Write(buffer, 0, read);
            }

            return memory.ToArray();
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or IOException)
        {
            return null;
        }
    }

    private static bool TryDecodeImage(
        string slot,
        string dataUrl,
        out ImportedImage? image)
    {
        image = null;

        if (!dataUrl.StartsWith("data:", StringComparison.Ordinal))
        {
            return false;
        }

        var comma = dataUrl.IndexOf(',');
        if (comma <= 5)
        {
            return false;
        }

        var metadata = dataUrl[5..comma];
        if (!metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        return TryDecodeImageBytes(slot, bytes, out image);
    }

    private static bool TryDecodeImageBytes(
        string slot,
        ReadOnlySpan<byte> bytes,
        out ImportedImage? image)
    {
        image = null;

        if (bytes.Length is 0 or > MaxImageBytes)
        {
            return false;
        }

        string mime;
        string extension;

        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47)
        {
            mime = "image/png";
            extension = "png";
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            mime = "image/jpeg";
            extension = "jpg";
        }
        else if (bytes.Length >= 4
            && bytes[0] == (byte)'G'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'8')
        {
            mime = "image/gif";
            extension = "gif";
        }
        else if (bytes.Length >= 12
            && bytes[0] == (byte)'R'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W'
            && bytes[9] == (byte)'E'
            && bytes[10] == (byte)'B'
            && bytes[11] == (byte)'P')
        {
            mime = "image/webp";
            extension = "webp";
        }
        else
        {
            return false;
        }

        image = new ImportedImage(slot, mime, extension, bytes.ToArray());
        return true;
    }

    /// <summary>
    /// Normal import rejects every protected-package marker. The container-level owner and locked
    /// markers are handled by <see cref="CvclkContainer"/>; this covers payload <c>editLock</c>.
    /// </summary>
    private static bool HasProtectionMarkers(JsonObject character)
    {
        return character["editLock"] is JsonValue value
            && value.TryGetValue<bool>(out var editLock)
            && editLock;
    }

    private static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? GetInt(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<double>(out var number)
            && number is >= int.MinValue and <= int.MaxValue
            && Math.Abs(number % 1) < 0.000001)
        {
            return (int)number;
        }

        return null;
    }
}