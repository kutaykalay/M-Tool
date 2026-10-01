using MTool.App.ViewModels;

namespace MTool.Tests.Fakes;

internal sealed class FakeStartupTask : IStartupTask
{
    /// <summary>The exe the registered task starts; "" for a task that is not ours; null when there is none.</summary>
    public string? RegisteredExe { get; set; }

    /// <summary>Thrown by every call.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Thrown by <see cref="Disable"/> only.</summary>
    public Exception? DisableFailure { get; set; }

    public List<string> Calls { get; } = [];

    public string? QueryRegisteredExe()
    {
        Calls.Add("query");
        return Failure is { } error ? throw error : RegisteredExe;
    }

    public void Enable(string exePath)
    {
        Calls.Add($"enable {exePath}");
        RegisteredExe = Failure is { } error ? throw error : exePath;
    }

    public void Disable()
    {
        Calls.Add("disable");
        RegisteredExe = (Failure ?? DisableFailure) is { } error ? throw error : null;
    }
}
