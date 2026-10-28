using System.Text;

namespace Tunly.Core;

/// <summary>Thread-safe writer for the core log file with simple size-based rotation.</summary>
public sealed class LogFileWriter : IDisposable
{
    readonly object _gate = new();
    readonly string _path;
    readonly long _maxBytes;
    StreamWriter? _writer;
    long _written;

    public LogFileWriter(string path, long maxBytes = 10 * 1024 * 1024)
    {
        _path = path;
        _maxBytes = maxBytes;
    }

    public string Path => _path;

    public void Write(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        lock (_gate)
        {
            try
            {
                _writer ??= Open();

                var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}";
                _writer.WriteLine(text);
                _written += Encoding.UTF8.GetByteCount(text) + Environment.NewLine.Length;

                if (_written >= _maxBytes) Rotate();
            }
            catch (Exception)
            {
                // Logging must never take down the engine.
            }
        }
    }

    StreamWriter Open()
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _written = 0;
        return new StreamWriter(
            new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    void Rotate()
    {
        try
        {
            _writer?.Dispose();
            _writer = null;

            var archived = _path + ".1";
            if (File.Exists(archived)) File.Delete(archived);
            if (File.Exists(_path)) File.Move(_path, archived);

            _writer = Open();
        }
        catch (Exception)
        {
            // Keep writing to the current handle if rotation fails.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _writer?.Dispose(); }
            catch (Exception) { /* Ignore. */ }
            _writer = null;
        }
    }
}
