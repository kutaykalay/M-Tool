using MTool.Core.Ec;

namespace MTool.Core.Diagnostics;

/// <summary>
/// The session's record of WMI2 experimental writes, for the EC data submission (plan asama15 15c-6). In memory
/// only: it is never written to disk and ends with M-Tool. Only writes that reached the EC are kept: a dry run, a
/// refusal or a register that already held the value is not a write. The WMI2 window records on the UI thread and
/// the submission reads off it, so every member takes the lock.
/// </summary>
public sealed class Wmi2WriteJournal
{
    /// <summary>The server takes at most this many; a write that changed nothing gives way first, then the oldest.</summary>
    public const int MaxWrites = 20;

    private readonly Lock _gate = new();
    private readonly List<Wmi2WriteRecord> _writes = [];
    private string? _firmware;
    private Wmi2LayoutResult? _layout;

    /// <summary>Control opened for <paramref name="firmware"/>; another firmware than before starts over.</summary>
    public void Opened(string firmware, Wmi2LayoutResult layout)
    {
        ArgumentNullException.ThrowIfNull(firmware);
        ArgumentNullException.ThrowIfNull(layout);
        lock (_gate)
        {
            if (_firmware != firmware)
            {
                _writes.Clear();
            }

            (_firmware, _layout) = (firmware, layout);
        }
    }

    /// <summary>Keeps the outcome when it was a write that reached the EC; ignored before control opened.</summary>
    public void Record(Wmi2WriteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (ToRecord(outcome) is not { } record)
        {
            return;
        }

        lock (_gate)
        {
            if (_firmware is null)
            {
                return;
            }

            _writes.Add(record);
            if (_writes.Count > MaxWrites)
            {
                // A write that changed nothing (the write-path test) gives way first, so real writes are kept longer.
                var unchanged = _writes.FindIndex(w => w.Before == w.After);
                _writes.RemoveAt(unchanged >= 0 ? unchanged : 0);
            }
        }
    }

    /// <summary>A copy of the session so far, with no answers; null before control opened or before the first write.</summary>
    public ExperimentalSession? Snapshot()
    {
        lock (_gate)
        {
            return (_firmware, _layout) is ({ } firmware, { } layout) && _writes.Count > 0
                ? new ExperimentalSession(firmware, layout.Status, layout.Verified, _writes.ToList().AsReadOnly(), [])
                : null;
        }
    }

    private static Wmi2WriteRecord? ToRecord(Wmi2WriteOutcome outcome)
    {
        if (outcome.Write is not { } write || outcome.Before is not { } before || FeatureOf(write.Register) is not { } feature)
        {
            return null;
        }

        Wmi2ReadBack? readBack = outcome.Status switch
        {
            WriteStatus.Applied => Wmi2ReadBack.Ok,
            WriteStatus.FailedRecovered or WriteStatus.FailedUnrecovered =>
                outcome.Problem == Wmi2GatewayProblem.WriteFailed ? Wmi2ReadBack.Failed : Wmi2ReadBack.Differs,
            _ => null, // a dry run or a refusal wrote nothing
        };
        return readBack is { } result ? new Wmi2WriteRecord(feature, before, write.Value, result) : null;
    }

    private static Wmi2Feature? FeatureOf(byte register) => register switch
    {
        Wmi2WriteCeiling.CoolerBoostRegister => Wmi2Feature.CoolerBoost,
        Wmi2WriteCeiling.PerformanceRegister => Wmi2Feature.PerformanceMode,
        Wmi2WriteCeiling.ChargeLimitRegister => Wmi2Feature.ChargeLimit,
        Wmi2WriteCeiling.FanModeRegister => Wmi2Feature.FanMode,
        _ => null,
    };
}
