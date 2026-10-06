using MTool.Core.Profiles;

namespace MTool.Core.Device.Config;

/// <summary>How far a device config has been checked on real hardware.</summary>
public enum DeviceStatus
{
    /// <summary>Taken from reference projects; read-only and experimental.</summary>
    Draft,

    /// <summary>Its owner confirmed the values M-Tool reads.</summary>
    ReadVerified,

    /// <summary>Writes were tested step by step on this exact firmware.</summary>
    WriteVerified,
}

/// <summary>Which MSI WMI interface reaches the EC.</summary>
public enum WmiInterface
{
    /// <summary>One field per register (MSI_CPU, MSI_VGA, ...): Intel 10th gen and older, most AMD.</summary>
    Wmi1,

    /// <summary>Packet methods on MSI_ACPI: Intel 11th gen and newer.</summary>
    Wmi2,
}

/// <summary>
/// One laptop model's EC layout, embedded as JSON (<c>MTool.Devices.&lt;id&gt;.json</c>) and checked
/// by <see cref="DeviceConfigValidator"/> before use. Hardware facts only; code keeps the safety
/// ceilings a config can never widen.
/// </summary>
/// <param name="Wmi1">The register-to-field map; null when the model has no WMI1 interface.</param>
/// <param name="PortRegisters">Registers WMI cannot reach that may go through the port (PawnIO), within <see cref="DeviceConfigValidator.PortCeiling"/>.</param>
/// <param name="Sources">Where each fact came from (dump, DSDT, reference project and commit).</param>
public sealed record DeviceConfig(
    int SchemaVersion,
    string Id,
    string DisplayName,
    DeviceStatus Status,
    FirmwareSpec Firmware,
    WmiInterface Interface,
    FirmwareLocation FirmwareLocation,
    IReadOnlyList<FanConfig> Fans,
    FeatureSet Features,
    Wmi1Layout? Wmi1,
    IReadOnlyList<byte> PortRegisters,
    CurveLimits Limits,
    IReadOnlyList<PresetConfig> Presets,
    IReadOnlyList<string> Sources);

/// <param name="Exact">Full firmware strings this record was checked on; writes need one of these.</param>
/// <param name="Family">First ten characters (<c>xxxxbMSn.y</c>) shared by related firmware, for read-only matching.</param>
public sealed record FirmwareSpec(IReadOnlyList<string> Exact, string? Family);

/// <summary>Where the EC keeps its firmware version and date strings.</summary>
public sealed record FirmwareLocation(byte Version, int VersionLength, byte Date, int DateLength);

public sealed record RegisterBlock(byte Start, int Count);

/// <summary>One fan: live values and its table. The RPM period is big-endian at <paramref name="RpmHigh"/> and the byte after it.</summary>
/// <param name="FactoryDownOffsets">The EC's factory down offsets (never written); required for writes and presets.</param>
/// <param name="FactoryCurve">The factory table; required for writes (recovery restores it).</param>
public sealed record FanConfig(
    string Id,
    byte Temperature,
    byte SpeedPercent,
    byte RpmHigh,
    RegisterBlock UpThresholds,
    RegisterBlock Speeds,
    IReadOnlyList<int>? FactoryDownOffsets,
    FanCurve? FactoryCurve);

/// <summary>Optional controls; a null feature does not exist on the model.</summary>
public sealed record FeatureSet(
    CoolerBoostFeature? CoolerBoost,
    ChargeLimitFeature? ChargeLimit,
    PerformanceModeFeature? PerformanceMode,
    FanModeFeature? FanMode);

public sealed record CoolerBoostFeature(byte Register, int Bit);

/// <summary>The register holds <c>1 &lt;&lt; EnableBit | percent</c>.</summary>
public sealed record ChargeLimitFeature(byte Register, int EnableBit, int Min, int Max);

/// <param name="Modes">In display order; each id names the mode in the UI and in settings.</param>
public sealed record PerformanceModeFeature(byte Register, IReadOnlyList<ModeValue> Modes);

public sealed record ModeValue(string Id, byte Value);

public sealed record FanModeFeature(byte Register, byte Auto, byte Advanced);

public sealed record Wmi1Layout(IReadOnlyList<WmiFieldSpec> Fields);

/// <summary>The WMI1 instance <c>Class[Index]</c> that reads and writes <paramref name="Register"/>.</summary>
public sealed record WmiFieldSpec(byte Register, string Class, int Index);

/// <summary>This model's fan table limits, inside the code's own ceiling.</summary>
public sealed record CurveLimits(int MinUpThresholdC, int MaxUpThresholdC, int MaxSpeedPercent);

/// <param name="Curves">One curve per fan, keyed by <see cref="FanConfig.Id"/>.</param>
public sealed record PresetConfig(string Name, IReadOnlyDictionary<string, FanCurve> Curves);
