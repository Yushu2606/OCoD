using System.Text;

namespace OCoD.Protocol;

/// <summary>One JSON object per UTF-8 line over a stream.</summary>
public sealed class JsonLineConnection : IAsyncDisposable
{
    private const int MaxLineLength = 32 * 1024 * 1024;

    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public JsonLineConnection(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        _writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
    }

    public async ValueTask WriteAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var line = ProtocolJson.Serialize(message);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask<IpcMessage?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            return null;
        }

        if (line.Length > MaxLineLength)
        {
            throw new InvalidDataException($"IPC line exceeded {MaxLineLength} characters.");
        }

        return ProtocolJson.Parse(line);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reader.Dispose();
        _writer.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);

        await _writeLock.WaitAsync().ConfigureAwait(false);
        _writeLock.Release();
        _writeLock.Dispose();
    }
}