namespace OCoD.Protocol;

/// <summary>A small thread-safe append-only log. Returns null when the path is not writable.</summary>
public sealed class LogFile : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    private LogFile(string path, StreamWriter writer)
    {
        Path = path;
        _writer = writer;
    }

    public string Path { get; }

    public static LogFile? TryOpen(string path)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new LogFile(
                path,
                new StreamWriter(path, append: true)
                {
                    AutoFlush = true,
                });
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{DateTimeOffset.UtcNow:O} {message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }
}