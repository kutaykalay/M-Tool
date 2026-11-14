using MTool.Core.Device;

namespace MTool.Core.Diagnostics;

/// <summary>A switch of WMI2 experimental control; each is one ceiling register (<see cref="Ec.Wmi2WriteCeiling"/>).</summary>
public enum Wmi2Feature
{
    CoolerBoost,
    PerformanceMode,
    ChargeLimit,
    FanMode,
}

/// <summary>How the bits of a WMI2 write read back.</summary>
public enum Wmi2ReadBack
{
    /// <summary>Read back as written.</summary>
    Ok,

    /// <summary>
    /// Read back as another value after every attempt, or the register moved to a byte the rules refuse between
    /// attempts (MSI Center or the EC may have set it back).
    /// </summary>
    Differs,

    /// <summary>The write call or the read after it failed.</summary>
    Failed,
}

/// <summary>The user's answer in the "Send EC data" preview: did the switch do what it says?</summary>
public enum Wmi2FeatureAnswer
{
    Worked,
    NotWorked,
    NotTried,
}

/// <summary>One write that reached the EC: the byte before, the byte written, and how it read back.</summary>
public sealed record Wmi2WriteRecord(Wmi2Feature Feature, byte Before, byte After, Wmi2ReadBack ReadBack);

public sealed record Wmi2FeatureFeedback(Wmi2Feature Feature, Wmi2FeatureAnswer Answer);

/// <summary>
/// What WMI2 experimental control did in this session, for the EC data submission (plan asama15 15a-2, 15c-6).
/// Kept in memory only (<see cref="Wmi2WriteJournal"/>); writes happen only on an exact firmware match.
/// </summary>
/// <param name="Firmware">The running firmware control opened for.</param>
/// <param name="LayoutStatus">The bytes the session's layout check read.</param>
/// <param name="Writes">Oldest first, at most <see cref="Wmi2WriteJournal.MaxWrites"/>.</param>
/// <param name="Feedback">The preview's answers; empty until the user answers.</param>
public sealed record ExperimentalSession(
    string Firmware,
    Wmi2Status LayoutStatus,
    bool LayoutVerified,
    IReadOnlyList<Wmi2WriteRecord> Writes,
    IReadOnlyList<Wmi2FeatureFeedback> Feedback)
{
    /// <summary>
    /// One question per feature a write changed, in the order first written. A write of the byte the register already
    /// held (the write-path test) changes nothing the user could see, so it asks nothing.
    /// </summary>
    public IReadOnlyList<Wmi2Feature> Questions =>
        Writes.Where(w => w.Before != w.After).Select(w => w.Feature).Distinct().ToList().AsReadOnly();
}
