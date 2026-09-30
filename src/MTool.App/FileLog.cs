using System.Diagnostics;
using System.IO;
using MTool.Core;

namespace MTool.App;

/// <summary>Daily log file under <c>%AppData%\M-Tool\logs</c>. Every EC write request lands here.</summary>
internal sealed class FileLog : IAppLog
{
    private readonly Lock _sync = new();

    public void Info(string message) => Append("INFO ", message);

    public void Warn(string message) => Append("WARN ", message);

    public void Error(string message, Exception? exception = null) =>
        Append("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Append(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                File.AppendAllText(Path.Combine(AppPaths.Logs, $"m-tool-{DateTime.Now:yyyyMMdd}.log"), line);
            }
        }
        catch (Exception ex)
        {
            // Logging must never take the app down; surface it to a debugger instead.
            Debug.WriteLine($"Log yazılamadı: {ex.Message}{Environment.NewLine}{line}");
        }
    }
}
