namespace MTool.Core.Power;

/// <summary>Where the laptop draws power from, as Windows reports it. Events may arrive on any thread.</summary>
public interface IPowerSource
{
    /// <summary>Null when Windows cannot tell.</summary>
    PowerSource? Current { get; }

    /// <summary>Raised when <see cref="Current"/> may have changed; it can arrive several times for one change.</summary>
    event Action? Changed;
}

/// <param name="Debounce">
/// Quiet time after the last change before acting, so a loose plug gives one switch, not several.
/// </param>
public sealed record PowerSwitchOptions(TimeSpan Debounce)
{
    public static PowerSwitchOptions Default { get; } = new(TimeSpan.FromSeconds(3));
}
