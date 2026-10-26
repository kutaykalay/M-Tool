using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Settings;

namespace MTool.Core.Ec;

/// <summary>
/// The WMI2 experimental write path (CLAUDE.md, "WMI2 deneysel yazma"; plan asama15 §4.3). Apart from the P65
/// path on purpose: its recovery ladder (factory fan table, port Cooler Boost) would do harm here. One change is
/// one <see cref="EcWorker"/> operation: the WMI2-only probe, the snapshot and the policy, then a read of the
/// register right before <c>Set_Data</c> that the plan and both checks share, so the self-changing low bits of 0x98
/// are never written back stale. Every attempt plans again from a fresh read, and a write is never retried blindly.
/// On failure the old value is written back under the same rules and every later WMI2 write of the session is
/// refused; nothing is persisted, there is no fan table and no port.
/// </summary>
public sealed partial class EcGateway
{
    private const string P65PlanOnWmi2Gateway = "WMI2 experimental gateway: P65 plans are refused.";

    private static readonly WritePolicy ClosedP65Policy = new(FirmwareSupported: false, PreStateSaved: false, DryRun: false, PortAvailable: false);

    private readonly Wmi2GatewaySetup? _wmi2;
    private string? _wmi2LockReason;
    private int _wmi2WritePathTested;

    /// <summary>The plan for a register given what it holds now; null when it already holds what is wanted.</summary>
    private delegate Wmi2Planned? Wmi2Planner(byte current, Wmi2GatewaySetup setup);

    public EcGatewayKind Kind => _wmi2 is null ? EcGatewayKind.P65Wmi1 : EcGatewayKind.Wmi2Experimental;

    /// <summary>The write-path test (K5) passed in this session; only then may a real change run.</summary>
    public bool IsWmi2WritePathTested => Volatile.Read(ref _wmi2WritePathTested) == 1;

    /// <summary>Set when a WMI2 write failed in this session (English, for the log); null while open.</summary>
    public string? Wmi2SessionLockReason => Volatile.Read(ref _wmi2LockReason);

    /// <summary>K5: writes the current performance byte again and reads it back.</summary>
    public Task<Wmi2WriteOutcome> TestWmi2WritePathAsync(CancellationToken cancellationToken = default) =>
        RunWmi2Async(Wmi2WriteCeiling.PerformanceRegister, Wmi2WriteIntent.WritePathTest, WritePathTestPlan, cancellationToken);

    public Task<Wmi2WriteOutcome> ApplyWmi2Async(Wmi2Change change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        return RunWmi2Async(change.Register, Wmi2WriteIntent.Change, (current, setup) => ChangePlan(change, current, setup), cancellationToken);
    }

    /// <summary>
    /// "Back to before M-Tool": every ceiling register on its own, in ceiling order, as a real change (so after the
    /// write-path test, like any other). A saved byte the rules do not allow is skipped and reported, a register that
    /// already holds it is left alone, and a failure locks the rest. The snapshot file is read on the caller's thread.
    /// </summary>
    public async Task<IReadOnlyList<Wmi2WriteOutcome>> RevertWmi2Async(CancellationToken cancellationToken = default)
    {
        var saved = _wmi2 is { } setup ? LoadWmi2PreState(setup) : null;
        var outcomes = new List<Wmi2WriteOutcome>();
        foreach (var register in Wmi2WriteCeiling.Registers)
        {
            if (_wmi2 is null || saved?.Registers.FirstOrDefault(r => r.Register == register) is not { } old)
            {
                outcomes.Add(Wmi2Reject(new Wmi2WriteOutcome(
                    WriteStatus.Rejected, register, Block: _wmi2 is null ? null : Wmi2WriteBlock.NoPreState,
                    Problem: _wmi2 is null ? Wmi2GatewayProblem.WrongGateway : null)));
                continue;
            }

            try
            {
                outcomes.Add(await RunWmi2Async(
                    register, Wmi2WriteIntent.Change, (current, s) => RevertPlan(register, old.Value, current, s), cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                // What was written back stays reported; this register and the rest were not attempted.
                outcomes.AddRange(Wmi2WriteCeiling.Registers.Skip(outcomes.Count)
                    .Select(r => new Wmi2WriteOutcome(WriteStatus.Rejected, r, Problem: Wmi2GatewayProblem.Cancelled)));
                break;
            }
        }

        return outcomes.AsReadOnly();
    }

    private async Task<Wmi2WriteOutcome> RunWmi2Async(
        byte register, Wmi2WriteIntent intent, Wmi2Planner planner, CancellationToken cancellationToken)
    {
        if (_wmi2 is not { } setup)
        {
            return Wmi2Reject(new Wmi2WriteOutcome(WriteStatus.Rejected, register, Problem: Wmi2GatewayProblem.WrongGateway));
        }

        // What needs no EC and no file is decided first, so a closed write reads nothing and takes no snapshot.
        if (Wmi2Policy(setup, preStateSaved: true, wmi2Only: true).Decide(intent) is { Kind: Wmi2WriteVerdictKind.Refused } early)
        {
            return Wmi2Reject(new Wmi2WriteOutcome(WriteStatus.Rejected, register, Block: early.Block));
        }

        try
        {
            return await _worker.RunWriteAsync(ec => Wmi2Locked(ec, setup, register, intent, planner), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Wmi2Locked handles every failure from its first write on, so nothing changed.
            SafeLog(log => log.Error($"WMI2 0x{register:X2}: EC access failed before writing", ex));
            return new Wmi2WriteOutcome(WriteStatus.Rejected, register, Problem: Wmi2GatewayProblem.Unreachable);
        }
    }

    private Wmi2WriteOutcome Wmi2Locked(
        IEcWritableRegisters ec, Wmi2GatewaySetup setup, byte register, Wmi2WriteIntent intent, Wmi2Planner planner)
    {
        var (verdict, preStateProblem) = Wmi2Verdict(ec, setup, intent);
        if (verdict.Kind == Wmi2WriteVerdictKind.Refused)
        {
            return Wmi2Reject(new Wmi2WriteOutcome(
                WriteStatus.Rejected, register, Block: verdict.Block,
                Problem: verdict.Block == Wmi2WriteBlock.NoPreState ? preStateProblem : null));
        }

        // The read the plan, the checks and the write share: nothing else touches the EC in between.
        var before = ec.Read(register);
        if (planner(before, setup) is not { } planned)
        {
            SafeLog(log => log.Info($"WMI2 0x{register:X2}: already 0x{before:X2}, nothing to write."));
            return new Wmi2WriteOutcome(WriteStatus.Applied, register, before);
        }

        if (planned.Write is not { } write)
        {
            return Wmi2Reject(new Wmi2WriteOutcome(WriteStatus.Rejected, register, before, Rejection: planned.Rejection));
        }

        if (verdict.Kind == Wmi2WriteVerdictKind.DryRun)
        {
            SafeLog(log => log.Info($"DRY-RUN WMI2 0x{register:X2}: 0x{before:X2} -> 0x{write.Value:X2}"));
            return new Wmi2WriteOutcome(WriteStatus.DryRun, register, before, write);
        }

        return Wmi2WriteOrFail(ec, setup, intent, planner, before, write);
    }

    /// <summary>
    /// The probe first, so a laptop that is not WMI2-only is neither read nor given a snapshot; then the snapshot,
    /// then the whole policy. The probe is not asked again for later attempts or the recovery, which only write
    /// the same change or the old value under the same rules.
    /// </summary>
    private (Wmi2WriteVerdict Verdict, Wmi2GatewayProblem? PreStateProblem) Wmi2Verdict(
        IEcRegisters ec, Wmi2GatewaySetup setup, Wmi2WriteIntent intent)
    {
        if (!AskWmi2Only(setup))
        {
            return (Wmi2Policy(setup, preStateSaved: true, wmi2Only: false).Decide(intent), null);
        }

        var problem = EnsureWmi2PreState(ec, setup);
        return (Wmi2Policy(setup, preStateSaved: problem is null, wmi2Only: true).Decide(intent), problem);
    }

    private Wmi2WriteOutcome Wmi2WriteOrFail(
        IEcWritableRegisters ec, Wmi2GatewaySetup setup, Wmi2WriteIntent intent, Wmi2Planner planner, byte before, RegisterWrite write)
    {
        var change = $"0x{write.Register:X2}: 0x{before:X2} -> 0x{write.Value:X2}";
        SafeLog(log => log.Info($"WRITE-WMI2 {change} (experimental, {setup.Firmware?.Version})"));
        Wmi2GatewayProblem? problem;
        Exception? error = null;
        try
        {
            problem = Wmi2WriteVerified(ec, setup, write, planner);
        }
        catch (Exception ex)
        {
            (problem, error) = (Wmi2GatewayProblem.WriteFailed, ex);
        }

        if (problem is { } failed)
        {
            return Wmi2Fail(ec, setup, write, before, failed, error);
        }

        if (intent == Wmi2WriteIntent.WritePathTest)
        {
            Volatile.Write(ref _wmi2WritePathTested, 1);
        }

        SafeLog(log => log.Info($"WMI2 {change}: applied and verified."));
        return new Wmi2WriteOutcome(WriteStatus.Applied, write.Register, before, write);
    }

    /// <summary>
    /// Up to three attempts, compared under the register's mask; null when the bits read back. An attempt after the
    /// first plans again from a fresh read and both checks, so a byte that moved in between is never written back
    /// stale. No silent-period retry as on the P65: a write is never repeated without this check.
    /// </summary>
    private Wmi2GatewayProblem? Wmi2WriteVerified(IEcWritableRegisters ec, Wmi2GatewaySetup setup, RegisterWrite first, Wmi2Planner planner)
    {
        var mask = Wmi2WriteRules.MaskOf(first.Register);
        var write = first;
        for (var attempt = 1; attempt <= WriteAttempts; attempt++)
        {
            if (attempt > 1)
            {
                var current = ec.Read(first.Register);
                if (planner(current, setup) is not { } again)
                {
                    return null;
                }

                if (again.Write is not { } next)
                {
                    SafeLog(log => log.Warn($"WMI2 0x{first.Register:X2} now holds 0x{current:X2}: {again.Rejection}, not written again."));
                    return Wmi2GatewayProblem.RegisterMoved;
                }

                write = next;
            }

            ec.Write(write.Register, write.Value);
            var actual = ec.Read(write.Register);
            if (((actual ^ write.Value) & mask) == 0)
            {
                return null;
            }

            var tried = write;
            SafeLog(log => log.Warn($"WMI2 {tried}: read back 0x{actual:X2} (attempt {attempt}/{WriteAttempts})."));
        }

        return Wmi2GatewayProblem.ReadBackDiffers;
    }

    /// <summary>Locks first, then the old value back under the same rules; logs last.</summary>
    private Wmi2WriteOutcome Wmi2Fail(
        IEcWritableRegisters ec, Wmi2GatewaySetup setup, RegisterWrite write, byte before, Wmi2GatewayProblem problem, Exception? error)
    {
        var reason = $"WMI2 write {write} failed ({problem})";
        Volatile.Write(ref _wmi2LockReason, reason);
        var restored = TryRestoreWmi2(ec, setup, write.Register, before);
        SafeLog(log => log.Error(
            $"{reason}; WMI2 writes locked for this session. Old value 0x{before:X2} {(restored ? "restored" : "NOT restored")}. "
            + "MSI Center may have set the value back.",
            error));
        return new Wmi2WriteOutcome(restored ? WriteStatus.FailedRecovered : WriteStatus.FailedUnrecovered, write.Register, before, write, Problem: problem);
    }

    private bool TryRestoreWmi2(IEcWritableRegisters ec, Wmi2GatewaySetup setup, byte register, byte before)
    {
        try
        {
            var current = ec.Read(register);
            if (RevertPlan(register, before, current, setup) is not { } restore)
            {
                return true;
            }

            if (restore.Write is null)
            {
                SafeLog(log => log.Error($"Recovery: 0x{before:X2} over 0x{current:X2} at 0x{register:X2} is outside the rules ({restore.Rejection})."));
                return false;
            }

            SafeLog(log => log.Warn($"Recovery: writing 0x{register:X2} back to 0x{before:X2}."));
            return Wmi2WriteVerified(ec, setup, restore.Write, (now, s) => RevertPlan(register, before, now, s)) is null;
        }
        catch (Exception ex)
        {
            SafeLog(log => log.Error($"Recovery: could not write 0x{register:X2} back", ex));
            return false;
        }
    }

    /// <summary>
    /// The gateway's second gate: the planner's byte is checked again against the same read. Null when the
    /// register already holds the change under its mask, so Cooler Boost is not rewritten for nothing.
    /// </summary>
    private static Wmi2Planned? ChangePlan(Wmi2Change change, byte current, Wmi2GatewaySetup setup)
    {
        var planned = Wmi2WriteRules.Plan(change, current, setup.List, setup.Firmware);
        if (planned.Write is not { } write)
        {
            return planned;
        }

        if (Wmi2WriteRules.Check(write, current, setup.List, setup.Firmware) is { } rejection)
        {
            return Wmi2Planned.Refused(rejection);
        }

        return ((write.Value ^ current) & Wmi2WriteRules.MaskOf(write.Register)) == 0 ? null : planned;
    }

    private static Wmi2Planned WritePathTestPlan(byte current, Wmi2GatewaySetup setup)
    {
        var write = new RegisterWrite(Wmi2WriteCeiling.PerformanceRegister, current);
        return Wmi2WriteRules.CheckWritePathTest(write, current, setup.List, setup.Firmware) is { } rejection
            ? Wmi2Planned.Refused(rejection)
            : Wmi2Planned.Of(write);
    }

    /// <summary>
    /// Back to <paramref name="saved"/>: for Cooler Boost only bit 7, over the low bits read now. Null when the
    /// register already holds it under its mask; otherwise the write the write-back rules allow (a charge limit
    /// under 50 % too), or why they do not.
    /// </summary>
    private static Wmi2Planned? RevertPlan(byte register, byte saved, byte current, Wmi2GatewaySetup setup)
    {
        var value = register == Wmi2WriteCeiling.CoolerBoostRegister
            ? (byte)((current & ~Wmi2WriteCeiling.CoolerBoostMask) | (saved & Wmi2WriteCeiling.CoolerBoostMask))
            : saved;
        if (value == current)
        {
            return null;
        }

        var write = new RegisterWrite(register, value);
        return Wmi2WriteRules.CheckRestore(write, current, setup.List, setup.Firmware) is { } rejection
            ? Wmi2Planned.Refused(rejection)
            : Wmi2Planned.Of(write);
    }

    /// <summary>The one place the WMI2 policy is built: the session's setup and this gateway's own state.</summary>
    private Wmi2WritePolicy Wmi2Policy(Wmi2GatewaySetup setup, bool preStateSaved, bool wmi2Only) => new(
        Wmi2Only: wmi2Only,
        FirmwareExact: IsExactFirmware(setup),
        Consented: setup.Consented,
        LayoutVerified: setup.Layout.Verified,
        PreStateSaved: preStateSaved,
        SessionLockReason: Volatile.Read(ref _wmi2LockReason),
        WritePathTested: IsWmi2WritePathTested,
        DryRun: setup.DryRun);

    private static bool IsExactFirmware(Wmi2GatewaySetup setup)
    {
        try
        {
            return setup.List.Match(setup.Firmware) == MatchKind.Exact;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private bool AskWmi2Only(Wmi2GatewaySetup setup)
    {
        try
        {
            return setup.IsWmi2Only();
        }
        catch (Exception ex)
        {
            SafeLog(log => log.Warn($"WMI2-only check failed, treated as not WMI2-only: {ex.Message}"));
            return false;
        }
    }

    /// <summary>
    /// Null when a valid snapshot for this firmware exists, taken now if there was none. A file that is
    /// unreadable or for another firmware is never replaced: it is left for a human to look at.
    /// </summary>
    private Wmi2GatewayProblem? EnsureWmi2PreState(IEcRegisters ec, Wmi2GatewaySetup setup)
    {
        if (setup.Firmware is not { } firmware)
        {
            return Wmi2GatewayProblem.PreStateNotTaken;
        }

        try
        {
            if (!setup.PreState.Exists)
            {
                var state = Wmi2PreStateCapture.Read(ec, firmware, (setup.Now ?? (() => DateTimeOffset.Now))());
                setup.PreState.SaveIfMissing(state);
                SafeLog(log => log.Info($"Pre-M-Tool WMI2 state saved ({state.Registers.Count} registers)."));
            }

            return setup.PreState.Load() switch
            {
                null => Wmi2GatewayProblem.PreStateUnreadable,
                { } saved when saved.FirmwareVersion != firmware.Version => Wmi2GatewayProblem.PreStateOtherFirmware,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SafeLog(log => log.Error("Could not take the pre-M-Tool WMI2 state; WMI2 writes refused", ex));
            return Wmi2GatewayProblem.PreStateNotTaken;
        }
    }

    private PreMToolState? LoadWmi2PreState(Wmi2GatewaySetup setup)
    {
        try
        {
            return setup.Firmware is { } firmware && setup.PreState.Load() is { } state && state.FirmwareVersion == firmware.Version ? state : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SafeLog(log => log.Error("Could not read the pre-M-Tool WMI2 state", ex));
            return null;
        }
    }

    private Wmi2WriteOutcome Wmi2Reject(Wmi2WriteOutcome outcome)
    {
        var reason = outcome.Rejection?.ToString() ?? outcome.Block?.ToString() ?? outcome.Problem?.ToString();
        SafeLog(log => log.Warn($"REJECT-WMI2 0x{outcome.Register:X2}: {reason}"));
        return outcome;
    }
}
