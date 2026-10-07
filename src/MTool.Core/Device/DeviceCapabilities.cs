using MTool.Core.Device.Config;
using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>
/// The controls a model has, from its <see cref="DeviceConfig"/>. The UI hides what is missing;
/// whether a control can be written is a separate question (<see cref="DeviceAccess.WriteMode"/>,
/// <see cref="DeviceAccess.PortFeaturesAvailable"/>).
/// </summary>
/// <param name="GpuFan">A second fan with its own temperature, RPM and curve.</param>
/// <param name="FanCurve">Fan tables can be written: needs the fan mode switch.</param>
/// <param name="PerformanceModes">In the record's order. Modes M-Tool has no name for yet are left out.</param>
public sealed record DeviceCapabilities(
    bool GpuFan, bool FanCurve, bool CoolerBoost, bool ChargeLimit, IReadOnlyList<PerformanceMode> PerformanceModes)
{
    private const string GpuFanId = "gpu";

    public static DeviceCapabilities From(DeviceConfig config)
    {
        var features = config.Features;
        return new DeviceCapabilities(
            GpuFan: config.Fans.Any(f => f.Id == GpuFanId),
            FanCurve: features.FanMode is not null,
            CoolerBoost: features.CoolerBoost is not null,
            ChargeLimit: features.ChargeLimit is not null,
            PerformanceModes: Array.AsReadOnly(
                [.. (features.PerformanceMode?.Modes ?? []).Select(m => ModeOf(m.Id)).OfType<PerformanceMode>()]));
    }

    /// <summary>The same as <see cref="Restrict(DesiredState, ICollection{string})"/> without the warnings: for comparing, not writing.</summary>
    public DesiredState Restrict(DesiredState desired) => Restrict(desired, []);

    /// <summary>
    /// <paramref name="desired"/> without the parts this model lacks, so no write plan is made for
    /// them. Each dropped part adds a warning. The fan table and fan mode are not dropped yet: a
    /// layout still needs the fan mode switch (<see cref="DeviceLayout.From"/>), so
    /// <see cref="FanCurve"/> is always true in a session. 7f must drop them when it relaxes that.
    /// </summary>
    public DesiredState Restrict(DesiredState desired, ICollection<string> warnings)
    {
        var restricted = desired;
        if (desired.Performance is { } mode && !PerformanceModes.Contains(mode))
        {
            warnings.Add($"Bu modelde \"{ModeNames.Of(mode)}\" performans modu yok; kayıtlı seçim yok sayıldı.");
            restricted = restricted with { Performance = null };
        }

        if (desired.ChargeLimitPercent is not null && !ChargeLimit)
        {
            warnings.Add("Bu modelde şarj limiti yok; kayıtlı şarj limiti yok sayıldı.");
            restricted = restricted with { ChargeLimitPercent = null };
        }

        return restricted;
    }

    private static PerformanceMode? ModeOf(string id) => id switch
    {
        "high" => PerformanceMode.High,
        "balanced" => PerformanceMode.Balanced,
        "eco" => PerformanceMode.Eco,
        _ => null,
    };
}
