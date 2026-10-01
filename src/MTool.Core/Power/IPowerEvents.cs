namespace MTool.Core.Power;

/// <summary>Windows sleep and wake, as seen by the app. Events may arrive on any thread.</summary>
public interface IPowerEvents
{
    event Action? Suspending;

    event Action? Resumed;
}
