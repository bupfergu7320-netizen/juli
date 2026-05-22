using System.IO;
using System.Text;

namespace JuliMvs.App;

public sealed class FileLogger
{
    private readonly string _logDirectory;
    private readonly object _sync = new();

    public FileLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    public void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        var path = Path.Combine(_logDirectory, $"{DateTime.Now:yyyyMMdd}.log");
        lock (_sync)
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}
