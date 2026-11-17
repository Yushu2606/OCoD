using System.Globalization;
using System.Text;

namespace OCoD.Protocol;

/// <summary>An exclusive file lock used to guarantee one daemon per library or runtime root.</summary>
public sealed class SingleInstanceLock : IDisposable
{
    private readonly FileStream _stream;

    private SingleInstanceLock(FileStream stream)
    {
        _stream = stream;
    }

    public static bool TryAcquire(
        string lockFilePath,
        out SingleInstanceLock? instance,
        out Exception? error)
    {
        instance = null;
        error = null;

        try
        {
            var directory = Path.GetDirectoryName(lockFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            stream.SetLength(0);
            var pidBytes = Encoding.UTF8.GetBytes(
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            stream.Write(pidBytes);
            stream.Flush(flushToDisk: true);

            instance = new SingleInstanceLock(stream);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception;
            return false;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}