using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.Core.Profiles;

/// <summary>
/// What the user wants the EC to hold. Saved in settings.json and written again after a reboot or
/// resume, because the EC resets its fan tables and performance mode then.
/// A null field is never written. Cooler Boost is deliberately not part of it: it is a temporary switch.
/// </summary>
public sealed record DesiredState(
    string FanProfile = DesiredState.DefaultProfileName,
    PerformanceMode? Performance = null,
    int? ChargeLimitPercent = null,
    FanMode? FanMode = null)
{
    public const string DefaultProfileName = "Default";

    public const int FullChargePercent = 100;

    /// <summary>
    /// First run (no settings.json, or no desired state in it). A saved choice, even an explicit
    /// null, always wins over these.
    /// </summary>
    public static DesiredState Default { get; } = new(
        DefaultProfileName, PerformanceMode.Balanced, ChargeLimitPercent: FullChargePercent);

    /// <summary>
    /// Fan table first: it is the part that keeps the laptop cool if a later write fails. The charge
    /// limit last: it is the only part that needs the raw port, so the WMI parts never wait on it.
    /// </summary>
    public IReadOnlyList<WritePlan> ToPlans(ProfileCatalog catalog)
    {
        var profile = ProfileOf(catalog);
        var plans = new List<WritePlan> { WritePlans.FanCurves(profile.Curves, profile.Name) };
        if (Performance is { } performance)
        {
            plans.Add(WritePlans.Performance(performance));
        }

        if (FanMode is { } fanMode)
        {
            plans.Add(WritePlans.Fan(fanMode));
        }

        if (ChargeLimitPercent is { } percent)
        {
            plans.Add(WritePlans.ChargeLimit(percent));
        }

        return plans.AsReadOnly();
    }

    /// <summary>
    /// The parts that go through WMI only. Automatic reapplying uses this: the EC keeps the charge
    /// limit across reboot and sleep (measured), so start-up and resume never need the raw port.
    /// </summary>
    public DesiredState WithoutPortParts() => this with { ChargeLimitPercent = null };

    /// <summary>
    /// A wanted charge limit that is not known (<see cref="ControlState.Port"/> null) counts as
    /// drift: the band must not claim the EC holds it. Reapplying reads it fresh.
    /// </summary>
    public StateDrift DriftFrom(ControlState actual, ProfileCatalog catalog) => new(
        FanTable: catalog.Find(FanProfile)?.Curves != actual.FanCurves,
        Performance: Performance is not null && Performance != actual.Performance,
        ChargeLimit: ChargeLimitPercent is not null && ChargeLimitPercent != actual.Port?.ChargeLimitPercent,
        FanMode: FanMode is not null && FanMode != actual.FanMode);

    private FanProfile ProfileOf(ProfileCatalog catalog) =>
        catalog.Find(FanProfile) ?? throw new ArgumentException($"Bilinmeyen fan profili: {ProfileNameRules.Printable(FanProfile)}", nameof(catalog));
}

/// <summary>Which parts of the EC differ from <see cref="DesiredState"/>.</summary>
public sealed record StateDrift(bool FanTable, bool Performance, bool ChargeLimit, bool FanMode)
{
    public bool Any => FanTable || Performance || ChargeLimit || FanMode;
}
