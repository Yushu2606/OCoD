using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OCoD.Formats;

public enum CvclkError
{
    NotCvclk,
    TooLarge,
    Corrupt,
    UnsupportedVersion,
    Protected,
}

public sealed class CvclkFormatException : Exception
{
    public CvclkFormatException(CvclkError error, string message)
        : base(message)
    {
        Error = error;
    }

    public CvclkFormatException(CvclkError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    public CvclkError Error { get; }
}

public sealed record CvclkReadResult(
    string PayloadJson,
    bool IsPlainJson,
    int HeaderVersion,
    bool Locked);

/// <summary>Reads and writes the cvclk-v2 container described in docs/specs/container-v2.md.</summary>
public static class CvclkContainer
{
    public const int MaxFileBytes = 209_715_200;
    public const int MaxPayloadBytes = 419_430_400;

    private static readonly byte[] Magic = "BCVCLK2\n"u8.ToArray();
    private static readonly byte[] TransportKey = Convert.FromHexString(
        "73ca62079026ebb61663058e0d16e32be688bc36a8677299b75251a27d6ea2ba");
    private static readonly byte[] HkdfInfo = "cvclk-v2"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<CvclkReadResult> ReadFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Character package was not found.", path);
        }

        if (info.Length > MaxFileBytes)
        {
            throw new CvclkFormatException(
                CvclkError.TooLarge,
                $"Character package is larger than {MaxFileBytes} bytes.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Read(bytes);
    }

    public static CvclkReadResult Read(ReadOnlySpan<byte> file)
    {
        if (file.Length == 0)
        {
            throw new CvclkFormatException(CvclkError.NotCvclk, "The file is empty.");
        }

        if (!file[..Math.Min(file.Length, Magic.Length)].SequenceEqual(Magic))
        {
            if (file.Length > MaxFileBytes)
            {
                throw new CvclkFormatException(CvclkError.TooLarge, "The JSON file is too large.");
            }

            try
            {
                var plainJson = StrictUtf8.GetString(file);
                using var document = JsonDocument.Parse(plainJson);
                return new CvclkReadResult(plainJson, IsPlainJson: true, HeaderVersion: 0, Locked: false);
            }
            catch (Exception exception) when (
                exception is JsonException or DecoderFallbackException)
            {
                throw new CvclkFormatException(
                    CvclkError.NotCvclk,
                    "The file is neither a cvclk container nor a JSON character file.",
                    exception);
            }
        }

        if (file.Length < Magic.Length + 4 + 16 + 28)
        {
            throw new CvclkFormatException(CvclkError.Corrupt, "The cvclk container is truncated.");
        }

        var headerLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            file.Slice(8, 4));
        if (headerLength > 4096)
        {
            throw new CvclkFormatException(
                CvclkError.Corrupt,
                "The cvclk header is longer than 4096 bytes.");
        }

        var headerStart = 12;
        var headerEnd = headerStart + (int)headerLength;
        if (headerEnd > file.Length)
        {
            throw new CvclkFormatException(CvclkError.Corrupt, "The cvclk header is truncated.");
        }

        var headerBytes = file.Slice(headerStart, (int)headerLength);
        var saltStart = headerEnd;
        if (saltStart + 16 > file.Length)
        {
            throw new CvclkFormatException(CvclkError.Corrupt, "The cvclk salt is truncated.");
        }

        var salt = file.Slice(saltStart, 16);
        var sealedBytes = file[(saltStart + 16)..];
        if (sealedBytes.Length < 28)
        {
            throw new CvclkFormatException(CvclkError.Corrupt, "The cvclk payload is truncated.");
        }

        var headerVersion = 0;
        var locked = false;

        try
        {
            using var headerDocument = JsonDocument.Parse(headerBytes.ToArray());
            var root = headerDocument.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format)
                || format.ValueKind != JsonValueKind.String
                || !string.Equals(format.GetString(), "bug-clicker", StringComparison.Ordinal))
            {
                throw new CvclkFormatException(
                    CvclkError.NotCvclk,
                    "The cvclk header format is not 'bug-clicker'.");
            }

            if (!root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out headerVersion)
                || headerVersion != 2)
            {
                throw new CvclkFormatException(
                    CvclkError.UnsupportedVersion,
                    "Only cvclk container version 2 is supported.");
            }

            if (root.TryGetProperty("locked", out var lockedElement)
                && lockedElement.ValueKind == JsonValueKind.True)
            {
                locked = true;
            }
        }
        catch (JsonException exception)
        {
            throw new CvclkFormatException(CvclkError.Corrupt, "The cvclk header is invalid JSON.", exception);
        }

        if (locked)
        {
            throw new CvclkFormatException(
                CvclkError.Protected,
                "Protected cvclk packages are not supported by this importer yet.");
        }

        var key = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            TransportKey,
            key,
            salt,
            HkdfInfo);

        var nonce = sealedBytes[..12];
        var tag = sealedBytes.Slice(12, 16);
        var ciphertext = sealedBytes[28..];
        var compressed = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, tagSizeInBytes: 16);
            aes.Decrypt(nonce, ciphertext, tag, compressed, headerBytes);
        }
        catch (CryptographicException exception)
        {
            throw new CvclkFormatException(
                CvclkError.Corrupt,
                "The cvclk payload could not be authenticated.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var payloadBytes = Decompress(compressed);

        try
        {
            var payload = StrictUtf8.GetString(payloadBytes);
            return new CvclkReadResult(payload, IsPlainJson: false, headerVersion, Locked: false);
        }
        catch (DecoderFallbackException exception)
        {
            throw new CvclkFormatException(
                CvclkError.Corrupt,
                "The cvclk payload is not valid UTF-8.",
                exception);
        }
    }

    private static byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;

            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxPayloadBytes)
                {
                    throw new CvclkFormatException(
                        CvclkError.TooLarge,
                        "The cvclk payload exceeds the decompressed size limit.");
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new CvclkFormatException(
                CvclkError.Corrupt,
                "The cvclk payload is not a valid gzip stream.",
                exception);
        }
    }
}