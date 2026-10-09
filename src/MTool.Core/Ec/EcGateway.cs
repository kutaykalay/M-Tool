using System.Globalization;
using MTool.Core.Device;
using MTool.Core.Localization;
using MTool.Core.Profiles;
using MTool.Core.Resources;
using static MTool.Core.Localization.UserAndLogText;

namespace MTool.Core.Ec;

/// <summary>
/// The single write path to the EC. Every plan passes the firmware/backup/persisted
/// lock, the register whitelist and value rules, and a whole-table safety check; it is then
/// written in safe order with read-back verification. On any failure the fan table is brought back
/// to a verified safe state (factory table, then Cooler Boost as a last attempt) and writes stay
/// locked, persisted through <c>persistLock</c> so a new session starts locked too. The whole plan
/// runs as one <see cref="EcWorker"/> operation, so the Access_EC lock is held from validation to
/// the final read-back. Cooler Boost and the charge limit go through the raw port: a plan for one
/// of them holds nothing else, is refused untouched when the port is closed, and fails if
/// <see cref="PortWriteGuard"/> sees other registers change around it.
/// </summary>
public sealed class EcGateway
{
    private const int WriteAttempts = 3;

    private readonly EcWorker _worker;
    private readonly WritePolicy _policy;
    private readonly IAppLog _log;
    private readonly Action<string>? _persistLock;
    private readonly EcAccessRetry _retry;
    private UserAndLogText? _lockReason;

    /// <param name="persistLock">Called with the reason when a write fails, to keep later sessions locked.</param>
    /// <param name="retry">How silent EC periods are ridden out; <see cref="EcAccessRetry.Default"/> if null.</param>
    public EcGateway(
        EcWorker worker, WritePolicy policy, IAppLog log, Action<string>? persistLock = null, EcAccessRetry? retry = null)
    {
        _worker = worker;
        _policy = policy;
        _log = log;
        _persistLock = persistLock;
        _retry = retry ?? EcAccessRetry.Default;
        _lockReason = InitialLockReason(policy);
    }

    public bool IsWriteEnabled => LockReason is null;

    /// <summary>Why writes are refused (firmware, missing backup, a failed write), or null when open. Read-only.</summary>
    public string? LockReason => Volatile.Read(ref _lockReason)?.Text;

    public bool IsDryRun => _policy.DryRun;

    /// <summary>Cooler Boost and the charge limit can be written (the raw port is open).</summary>
    public bool IsPortAvailable => _policy.PortAvailable;

    public async Task<WriteOutcome> ApplyAsync(WritePlan plan, CancellationToken cancellationToken = default)
    {
        // Snapshot: the caller's list must not change between validation and writing.
        var snapshot = plan with { Writes = plan.Writes.ToArray().AsReadOnly() };

        if (Volatile.Read(ref _lockReason) is { } reason)
        {
            return Reject(snapshot, Say(nameof(CoreStrings.Write_Locked), reason));
        }

        if (CheckShape(snapshot, _policy.PortAvailable) is { } shapeError)
        {
            return Reject(snapshot, shapeError);
        }

        try
        {
            return await _worker.RunWriteAsync(ec => ApplyLocked(WithRetry(ec), snapshot), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ApplyLocked handles every failure after its first write, so this is a failure before
            // any write (lock busy, sleep, unreadable state): nothing changed.
            SafeLog(log => log.Error($"{snapshot.Description}: EC access failed before writing", ex));
            return Reject(snapshot, Say(nameof(CoreStrings.Write_Unreachable), ex.Message));
        }
    }

    /// <summary>Every EC access of a plan, including validation reads and recovery, rides out silent periods.</summary>
    private RetryingEcRegisters WithRetry(IEcWritableRegisters ec) =>
        new(ec, _retry, message => SafeLog(log => log.Warn(message)));

    private static UserAndLogText? InitialLockReason(WritePolicy policy) =>
        !policy.LayoutVerified ? Say(nameof(CoreStrings.Write_LockUnverifiedRecord))
        : !policy.FirmwareSupported ? Say(nameof(CoreStrings.Write_LockFirmware), EcMap.SupportedFirmware)
        : !policy.PreStateSaved ? Say(nameof(CoreStrings.Write_LockNoBackup))
        : policy.PersistedLockReason is { } persisted ? Plain(persisted)
        : null;

    private static UserAndLogText? CheckShape(WritePlan plan, bool portAvailable)
    {
        if (plan.Writes.Count == 0)
        {
            return Say(nameof(CoreStrings.Write_EmptyPlan));
        }

        if (plan.Writes.GroupBy(w => w.Register).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            return Say(nameof(CoreStrings.Write_DuplicateRegister), duplicate.Key);
        }

        return plan.Writes.Select(EcWriteRules.CheckStatic).FirstOrDefault(e => e is not null) is { } ruleError
            ? Plain(ruleError)
            : CheckPortRules(plan, portAvailable);
    }

    /// <summary>One port register per plan, so a side effect is never blamed on the wrong write.</summary>
    private static UserAndLogText? CheckPortRules(WritePlan plan, bool portAvailable)
    {
        if (plan.Writes.FirstOrDefault(w => WmiMap.PortRegisters.Contains(w.Register)) is not { } portWrite)
        {
            return null;
        }

        return !portAvailable ? Say(nameof(CoreStrings.Write_PortUnavailable), portWrite)
            : plan.Writes.Count > 1 ? Say(nameof(CoreStrings.Write_PortAlone), portWrite)
            : null;
    }

    private WriteOutcome ApplyLocked(IEcWritableRegisters ec, WritePlan plan)
    {
        // A plan queued before another one failed must not run on the EC that just failed.
        if (Volatile.Read(ref _lockReason) is { } reason)
        {
            return Reject(plan, Say(nameof(CoreStrings.Write_Locked), reason));
        }

        // Before the first port access (validation reads 0x98 through it). If this throws,
        // nothing has changed yet and ApplyAsync rejects the plan.
        var watched = PortWriteGuard.Guards(plan) && !_policy.DryRun ? PortWriteGuard.Snapshot(ec) : null;
        if (ValidateAgainstEc(ec, plan, watched) is { } refused)
        {
            return refused;
        }

        var ordered = Ordered(plan.Writes);
        if (_policy.DryRun)
        {
            SafeLog(log => log.Info($"DRY-RUN {plan.Description}: {string.Join(' ', ordered)}"));
            return new WriteOutcome(WriteStatus.DryRun, ordered, Say(nameof(CoreStrings.Write_DryRun)).Text);
        }

        SafeLog(log => log.Info($"WRITE {plan.Description}: {string.Join(' ', ordered)}"));
        try
        {
            var failed = ordered.FirstOrDefault(w => !WriteVerified(ec, w))
                ?? ordered.FirstOrDefault(w => ec.Read(w.Register) != w.Value);
            if (failed is not null)
            {
                return Fail(ec, plan, Say(nameof(CoreStrings.Write_ReadBackDiffers), failed), exception: null, watched);
            }

            if (watched is not null && PortWriteGuard.Check(ec, watched).Any)
            {
                return Fail(ec, plan, Say(nameof(CoreStrings.Write_OthersChanged)), exception: null, watched);
            }
        }
        catch (Exception ex)
        {
            return Fail(ec, plan, Plain(ex.Message), ex, watched);
        }

        SafeLog(log => log.Info($"{plan.Description}: applied and verified."));
        return new WriteOutcome(WriteStatus.Applied, ordered, Say(nameof(CoreStrings.Write_Applied)).Text);
    }

    /// <summary>
    /// A rejection, a failure, or null when the plan may be written. A port read that fails during
    /// validation is a rejection, unless the watched registers changed around it: then it is a
    /// failure like any disturbing port write.
    /// </summary>
    private WriteOutcome? ValidateAgainstEc(IEcWritableRegisters ec, WritePlan plan, IReadOnlyDictionary<byte, byte>? watched)
    {
        try
        {
            return CheckAgainstEc(ec, plan) is { } error ? Reject(plan, error) : null;
        }
        catch (Exception ex) when (watched is not null)
        {
            if (!PortWriteGuard.Check(ec, watched).Any)
            {
                throw;
            }

            return Fail(ec, plan, Say(nameof(CoreStrings.Write_ReadBackFailed), ex.Message), ex, watched);
        }
    }

    /// <summary>
    /// Checks that need live EC state. Any plan that touches the fan table or switches the fan
    /// mode must leave both curves valid, even if the unsafe part is already in the EC.
    /// </summary>
    private static UserAndLogText? CheckAgainstEc(IEcRegisters ec, WritePlan plan)
    {
        var liveError = plan.Writes
            .Where(w => w.Register == EcMap.CoolerBoost)
            .Select(w => EcWriteRules.CheckAgainstCurrent(w, ec.Read(w.Register)))
            .FirstOrDefault(e => e is not null) is { } liveRule ? Plain(liveRule) : null;
        var affectsFans = plan.Writes.Any(w => EcWriteRules.IsFanTable(w.Register) || w.Register == EcMap.FanMode);
        if (liveError is not null || !affectsFans)
        {
            return liveError;
        }

        var overlay = plan.Writes.ToDictionary(w => w.Register, w => w.Value);
        return TablesError(ec, overlay);
    }

    private static FanCurve ProjectCurve(IEcRegisters ec, FanRegisters fan, IReadOnlyDictionary<byte, byte> overlay)
    {
        IReadOnlyList<byte> Block(byte start, int count) =>
            Enumerable.Range(start, count)
                .Select(r => overlay.TryGetValue((byte)r, out var planned) ? planned : ec.Read((byte)r))
                .ToArray();

        return FanTableCodec.Decode(
            Block(fan.UpThresholdsStart, EcMap.ThresholdCount),
            Block(fan.SpeedsStart, EcMap.SpeedCount));
    }

    /// <summary>Both curves as the EC would hold them after <paramref name="overlay"/>, checked against the factory down offsets.</summary>
    private static UserAndLogText? TablesError(IEcRegisters ec, IReadOnlyDictionary<byte, byte> overlay) =>
        CurveError("CPU", ProjectCurve(ec, EcMap.CpuFan, overlay), FactoryDefaults.CpuDownOffsets)
        ?? CurveError("GPU", ProjectCurve(ec, EcMap.GpuFan, overlay), FactoryDefaults.GpuDownOffsets);

    private static UserAndLogText? CurveError(string fanName, FanCurve curve, IReadOnlyList<int> downOffsets) =>
        CurveValidator.Validate(curve, downOffsets) is [var first, ..]
            ? Say(
                nameof(CoreStrings.Write_CurveUnsafe),
                fanName,
                new UserAndLogText(first, CurveValidator.Validate(curve, downOffsets, CultureInfo.InvariantCulture)[0]))
            : null;

    private static IReadOnlyList<RegisterWrite> Ordered(IEnumerable<RegisterWrite> writes) =>
        writes.OrderBy(w => EcWriteRules.WriteOrder(w.Register)).ThenBy(w => w.Register).ToArray().AsReadOnly();

    private bool WriteVerified(IEcWritableRegisters ec, RegisterWrite write)
    {
        for (var attempt = 1; attempt <= WriteAttempts; attempt++)
        {
            ec.Write(write.Register, write.Value);
            var actual = ec.Read(write.Register);
            if (actual == write.Value)
            {
                return true;
            }

            SafeLog(log => log.Warn($"{write}: read back 0x{actual:X2} (attempt {attempt}/{WriteAttempts})."));
        }

        return false;
    }

    /// <summary>Locks first, recovers second, logs last: nothing here may skip the recovery.</summary>
    /// <param name="watched">For a port plan, the registers read before its first port access.</param>
    private WriteOutcome Fail(
        IEcWritableRegisters ec, WritePlan plan, UserAndLogText reason, Exception? exception, IReadOnlyDictionary<byte, byte>? watched)
    {
        var lockReason = Say(nameof(CoreStrings.Write_LockAfterFailure), plan.Description, reason);
        Volatile.Write(ref _lockReason, lockReason);
        PersistLock(lockReason.Text);

        // A half-finished port handshake is the likeliest source of damage, so check on every failure.
        var disturbance = watched is null ? new PortDisturbance([], Unreadable: false) : PortWriteGuard.Check(ec, watched);
        var ordered = Ordered(plan.Writes);
        var touchedFanTable = disturbance.TouchesFanTable || ordered.Any(w => EcWriteRules.IsFanTable(w.Register));
        var safe = TryEnsureSafeFanTable(ec, touchedFanTable);
        var restored = watched is null ? Plain("") : RestoreDisturbedModes(ec, watched, disturbance);
        var boosted = !safe && TryCoolerBoost(ec);

        var details = Join(' ', new[] { reason, Plain(disturbance.Describe()), restored }.Where(t => t.Text.Length > 0));
        SafeLog(log => log.Error($"{plan.Description}: write failed, EC writes locked. {details.Log}", exception));
        if (safe)
        {
            return new WriteOutcome(WriteStatus.FailedRecovered, ordered, Say(nameof(CoreStrings.Write_Failed), details).Text);
        }

        SafeLog(log => log.Error($"Safe fan table not verified; Cooler Boost {(boosted ? "turned on" : "could not be turned on either")}."));
        return new WriteOutcome(WriteStatus.FailedUnrecovered, ordered, Say(nameof(CoreStrings.Write_Failed), details).Text);
    }

    /// <summary>Puts a disturbed performance or fan mode back to its value before the port access.</summary>
    private UserAndLogText RestoreDisturbedModes(
        IEcWritableRegisters ec, IReadOnlyDictionary<byte, byte> watched, PortDisturbance disturbance)
    {
        var restores = disturbance.Changes
            .Where(c => !EcWriteRules.IsFanTable(c.Register))
            .Select(c => new RegisterWrite(c.Register, watched[c.Register]))
            .Where(w => EcWriteRules.CheckStatic(w) is null)
            .ToArray();
        if (restores.Length == 0)
        {
            return Plain("");
        }

        var list = string.Join(' ', restores.Select(w => w.ToString()));
        try
        {
            SafeLog(log => log.Warn($"Recovery: restoring {list}."));
            return restores.All(w => WriteVerified(ec, w)) ? Say(nameof(CoreStrings.Write_Restored), list) : Say(nameof(CoreStrings.Write_NotRestored), list);
        }
        catch (Exception ex)
        {
            SafeLog(log => log.Error($"Recovery: could not restore {list}", ex));
            return Say(nameof(CoreStrings.Write_NotRestored), list);
        }
    }

    private bool TryEnsureSafeFanTable(IEcWritableRegisters ec, bool touchedFanTable)
    {
        try
        {
            var unchanged = new Dictionary<byte, byte>();
            if (!touchedFanTable && TablesError(ec, unchanged) is null)
            {
                return true;
            }

            // Factory values are known safe, so rewriting them never makes the table worse.
            SafeLog(log => log.Warn("Recovery: writing the factory fan table."));
            var factory = Ordered(WritePlans.FanCurves(FactoryDefaults.FanCurves, "Default").Writes);
            return factory.All(w => WriteVerified(ec, w)) && factory.All(w => ec.Read(w.Register) == w.Value);
        }
        catch (Exception ex)
        {
            SafeLog(log => log.Error("Recovery: could not write the factory fan table", ex));
            return false;
        }
    }

    /// <summary>Read-modify-write of 0x98 only when two reads agree, so a garbled byte is never written back.</summary>
    private bool TryCoolerBoost(IEcWritableRegisters ec)
    {
        if (!_policy.PortAvailable)
        {
            SafeLog(log => log.Error("Recovery: port closed (WMI only), Cooler Boost not tried."));
            return false;
        }

        try
        {
            var (first, second) = (ec.Read(EcMap.CoolerBoost), ec.Read(EcMap.CoolerBoost));
            if (first != second)
            {
                SafeLog(log => log.Error($"Recovery: 0x98 read inconsistently (0x{first:X2} / 0x{second:X2}), Cooler Boost not written."));
                return false;
            }

            return WriteVerified(ec, WritePlans.CoolerBoost(on: true, first).Writes[0]);
        }
        catch (Exception ex)
        {
            SafeLog(log => log.Error("Recovery: could not turn on Cooler Boost", ex));
            return false;
        }
    }

    private void PersistLock(string reason)
    {
        try
        {
            _persistLock?.Invoke(reason);
        }
        catch (Exception ex)
        {
            SafeLog(log => log.Error("Could not save the write lock", ex));
        }
    }

    /// <summary>Logging must never interrupt a write, a recovery or an outcome.</summary>
    private void SafeLog(Action<IAppLog> write)
    {
        try
        {
            write(_log);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"M-Tool log hatası: {ex.Message}");
        }
    }

    private WriteOutcome Reject(WritePlan plan, UserAndLogText reason)
    {
        SafeLog(log => log.Warn($"REJECT {plan.Description}: {reason.Log}"));
        return new WriteOutcome(WriteStatus.Rejected, plan.Writes, reason.Text);
    }
}
