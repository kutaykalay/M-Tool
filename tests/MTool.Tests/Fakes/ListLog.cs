using System.Collections.Concurrent;
using MTool.Core;

namespace MTool.Tests.Fakes;

internal sealed class ListLog : IAppLog
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public void Info(string message) => Lines.Enqueue($"INFO {message}");

    public void Warn(string message) => Lines.Enqueue($"WARN {message}");

    public void Error(string message, Exception? exception = null) =>
        Lines.Enqueue($"ERROR {message} {exception?.Message}");
}
