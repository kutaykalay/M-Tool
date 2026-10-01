using MTool.Core.Power;

namespace MTool.Tests.Fakes;

/// <summary>Power events raised by hand.</summary>
internal sealed class FakePowerEvents : IPowerEvents
{
    public event Action? Suspending;

    public event Action? Resumed;

    public bool HasSubscribers => Suspending is not null || Resumed is not null;

    public void Suspend() => Suspending?.Invoke();

    public void Resume() => Resumed?.Invoke();
}
