using MTool.Core.Device;
using MTool.Core.Profiles;

namespace MTool.Core.Ec;

/// <summary>
/// The single write path to the EC (plan.md §4). Every plan passes the firmware/backup/persisted
/// lock, the register whitelist and value rules, and a whole-table safety check; it is then
/// written in safe order with read-back verification. On any failure the fan table is brought back
/// to a verified safe state (factory table, then Cooler Boost as a last attempt) and writes stay
/// locked, persisted through <c>persistLock</c> so a new session starts locked too. The whole plan
/// runs as one <see cref="EcWorker"/> operation, so the Access_EC lock is held from validation to
/// the final read-back.
/// </summary>
public sealed class EcGateway
{
    private const int WriteAttempts = 3;

    private readonly EcWorker _worker;
    private readonly WritePolicy _policy;
    private readonly IAppLog _log;
    private readonly Action<string>? _persistLock;
    private readonly EcAccessRetry _retry;
    private string? _lockReason;

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
    public string? LockReason => Volatile.Read(ref _lockReason);

    public bool IsDryRun => _policy.DryRun;

    public async Task<WriteOutcome> ApplyAsync(WritePlan plan, CancellationToken cancellationToken = default)
    {
        // Snapshot: the caller's list must not change between validation and writing.
        var snapshot = plan with { Writes = plan.Writes.ToArray().AsReadOnly() };

        if (Volatile.Read(ref _lockReason) is { } reason)
        {
            return Reject(snapshot, $"EC yazma kapalı: {reason}");
        }

        if (CheckShape(snapshot) is { } shapeError)
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
            SafeLog(log => log.Error($"{snapshot.Description}: yazmadan önce EC erişimi başarısız", ex));
            return Reject(snapshot, $"EC erişilemedi: {ex.Message}");
        }
    }

    /// <summary>Every EC access of a plan, including validation reads and recovery, rides out silent periods.</summary>
    private RetryingEcRegisters WithRetry(IEcWritableRegisters ec) =>
        new(ec, _retry, message => SafeLog(log => log.Warn(message)));

    private static string? InitialLockReason(WritePolicy policy) =>
        !policy.FirmwareSupported ? $"tanınmayan firmware (yalnızca {EcMap.SupportedFirmware} destekleniyor)"
        : !policy.PreStateSaved ? "M-Tool öncesi durum yedeği yok ya da geçersiz"
        : policy.PersistedLockReason;

    private static string? CheckShape(WritePlan plan)
    {
        if (plan.Writes.Count == 0)
        {
            return "Plan boş.";
        }

        if (plan.Writes.GroupBy(w => w.Register).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            return $"0x{duplicate.Key:X2} planda birden fazla kez var.";
        }

        return plan.Writes.Select(EcWriteRules.CheckStatic).FirstOrDefault(e => e is not null);
    }

    private WriteOutcome ApplyLocked(IEcWritableRegisters ec, WritePlan plan)
    {
        // A plan queued before another one failed must not run on the EC that just failed.
        if (Volatile.Read(ref _lockReason) is { } reason)
        {
            return Reject(plan, $"EC yazma kapalı: {reason}");
        }

        if (CheckAgainstEc(ec, plan) is { } error)
        {
            return Reject(plan, error);
        }

        var ordered = Ordered(plan.Writes);
        if (_policy.DryRun)
        {
            SafeLog(log => log.Info($"DRY-RUN {plan.Description}: {string.Join(' ', ordered)}"));
            return new WriteOutcome(WriteStatus.DryRun, ordered, "Dry-run: doğrulandı, yazılmadı.");
        }

        SafeLog(log => log.Info($"WRITE {plan.Description}: {string.Join(' ', ordered)}"));
        try
        {
            var failed = ordered.FirstOrDefault(w => !WriteVerified(ec, w))
                ?? ordered.FirstOrDefault(w => ec.Read(w.Register) != w.Value);
            if (failed is not null)
            {
                return Fail(ec, plan, ordered, $"{failed} doğrulanamadı.", exception: null);
            }
        }
        catch (Exception ex)
        {
            return Fail(ec, plan, ordered, ex.Message, ex);
        }

        SafeLog(log => log.Info($"{plan.Description}: uygulandı ve doğrulandı."));
        return new WriteOutcome(WriteStatus.Applied, ordered, "Uygulandı ve doğrulandı.");
    }

    /// <summary>
    /// Checks that need live EC state. Any plan that touches the fan table or switches the fan
    /// mode must leave both curves valid, even if the unsafe part is already in the EC.
    /// </summary>
    private static string? CheckAgainstEc(IEcRegisters ec, WritePlan plan)
    {
        var liveError = plan.Writes
            .Where(w => w.Register == EcMap.CoolerBoost)
            .Select(w => EcWriteRules.CheckAgainstCurrent(w, ec.Read(w.Register)))
            .FirstOrDefault(e => e is not null);
        var affectsFans = plan.Writes.Any(w => EcWriteRules.IsFanTable(w.Register) || w.Register == EcMap.FanMode);
        if (liveError is not null || !affectsFans)
        {
            return liveError;
        }

        var overlay = plan.Writes.ToDictionary(w => w.Register, w => w.Value);
        return CurveError("CPU", ProjectCurve(ec, EcMap.CpuFan, overlay))
            ?? CurveError("GPU", ProjectCurve(ec, EcMap.GpuFan, overlay));
    }

    private static FanCurve ProjectCurve(IEcRegisters ec, FanRegisters fan, IReadOnlyDictionary<byte, byte> overlay)
    {
        IReadOnlyList<byte> Block(byte start, int count) =>
            Enumerable.Range(start, count)
                .Select(r => overlay.TryGetValue((byte)r, out var planned) ? planned : ec.Read((byte)r))
                .ToArray();

        return FanTableCodec.Decode(
            Block(fan.UpThresholdsStart, EcMap.ThresholdCount),
            Block(fan.SpeedsStart, EcMap.SpeedCount),
            Block(fan.DownOffsetsStart, EcMap.ThresholdCount));
    }

    private static string? CurveError(string fanName, FanCurve curve) =>
        CurveValidator.Validate(curve) is [var first, ..] ? $"{fanName} eğrisi güvensiz olur: {first}" : null;

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

            SafeLog(log => log.Warn($"{write}: geri okunan 0x{actual:X2} (deneme {attempt}/{WriteAttempts})."));
        }

        return false;
    }

    /// <summary>Locks first, recovers second, logs last: nothing here may skip the recovery.</summary>
    private WriteOutcome Fail(
        IEcWritableRegisters ec, WritePlan plan, IReadOnlyList<RegisterWrite> ordered, string reason, Exception? exception)
    {
        var lockReason = $"yazma başarısız oldu: {plan.Description}, {reason}";
        Volatile.Write(ref _lockReason, lockReason);
        PersistLock(lockReason);

        var touchedFanTable = ordered.Any(w => EcWriteRules.IsFanTable(w.Register));
        var safe = TryEnsureSafeFanTable(ec, touchedFanTable);
        var boosted = !safe && TryCoolerBoost(ec);

        SafeLog(log => log.Error($"{plan.Description}: yazma başarısız, EC yazma kilitlendi. {reason}", exception));
        if (safe)
        {
            return new WriteOutcome(WriteStatus.FailedRecovered, ordered, $"Yazma başarısız, fan tablosu güvenli. {reason}");
        }

        SafeLog(log => log.Error($"Güvenli fan tablosu doğrulanamadı; Cooler Boost {(boosted ? "açıldı" : "da açılamadı")}."));
        return new WriteOutcome(WriteStatus.FailedUnrecovered, ordered, $"Yazma başarısız, fan tablosu doğrulanamadı. {reason}");
    }

    private bool TryEnsureSafeFanTable(IEcWritableRegisters ec, bool touchedFanTable)
    {
        try
        {
            var unchanged = new Dictionary<byte, byte>();
            if (!touchedFanTable
                && CurveError("CPU", ProjectCurve(ec, EcMap.CpuFan, unchanged)) is null
                && CurveError("GPU", ProjectCurve(ec, EcMap.GpuFan, unchanged)) is null)
            {
                return true;
            }

            // Factory values are known safe, so rewriting them never makes the table worse.
            SafeLog(log => log.Warn("Kurtarma: fabrika fan tablosu yazılıyor."));
            var factory = Ordered(WritePlans.FanCurves(FactoryDefaults.FanCurves, "Default").Writes);
            return factory.All(w => WriteVerified(ec, w)) && factory.All(w => ec.Read(w.Register) == w.Value);
        }
        catch (Exception ex)
        {
            SafeLog(log => log.Error("Kurtarma: fabrika fan tablosu yazılamadı", ex));
            return false;
        }
    }

    /// <summary>Read-modify-write of 0x98 only when two reads agree, so a garbled byte is never written back.</summary>
    private bool TryCoolerBoost(IEcWritableRegisters ec)
    {
        try
        {
            var (first, second) = (ec.Read(EcMap.CoolerBoost), ec.Read(EcMap.CoolerBoost));
            if (first != second)
            {
                SafeLog(log => log.Error($"Kurtarma: 0x98 tutarsız okundu (0x{first:X2} / 0x{second:X2}), Cooler Boost yazılmadı."));
                return false;
            }

            return WriteVerified(ec, WritePlans.CoolerBoost(on: true, first).Writes[0]);
        }
        catch (Exception ex)
        {
            SafeLog(log => log.Error("Kurtarma: Cooler Boost açılamadı", ex));
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
            SafeLog(log => log.Error("Yazma kilidi kalıcı olarak kaydedilemedi", ex));
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

    private WriteOutcome Reject(WritePlan plan, string reason)
    {
        SafeLog(log => log.Warn($"REJECT {plan.Description}: {reason}"));
        return new WriteOutcome(WriteStatus.Rejected, plan.Writes, reason);
    }
}
