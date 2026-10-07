using System.Diagnostics;
using System.IO;
using MTool.Core;
using MTool.Core.Diagnostics;

namespace MTool.App;

/// <summary>
/// Daily log file under <c>%AppData%\M-Tool\logs</c>. Every EC write request lands here. The user's
/// profile folder is masked, so the file can be attached to a public issue.
/// </summary>
internal sealed class FileLog : IAppLog
{
    private readonly Lock _sync = new();
    private readonly string _folder;
    private readonly PrivatePaths _privatePaths;

    public FileLog()
        : this(AppPaths.Logs, PrivatePaths.ForCurrentUser())
    {
    }

    internal FileLog(string folder, PrivatePaths privatePaths)
    {
        _folder = folder;
        _privatePaths = privatePaths;
    }

    public void Info(string message) => Append("INFO ", message);

    public void Warn(string message) => Append("WARN ", message);

    public void Error(string message, Exception? exception = null) =>
        Append("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Append(string level, string message)
    {
        string? line = null;
        try
        {
            line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {_privatePaths.Mask(message)}{Environment.NewLine}";
            lock (_sync)
            {
                Directory.CreateDirectory(_folder);
                File.AppendAllText(Path.Combine(_folder, $"m-tool-{DateTime.Now:yyyyMMdd}.log"), line);
            }
        }
        catch (Exception ex)
        {
            // Logging must never take the app down; surface it to a debugger instead.
            Debug.WriteLine($"Log yazılamadı: {ex.Message}{Environment.NewLine}{line}");
        }
    }
}
