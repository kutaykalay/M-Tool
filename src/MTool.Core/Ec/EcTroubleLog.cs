namespace MTool.Core.Ec;

/// <summary>
/// Writes EC retry reports to the app log without flooding it. Failures and write retries are
/// logged one by one; reads that a retry rescued are only counted and summarised once per
/// interval (the 1 Hz sensor poll would otherwise add a line almost every second).
/// </summary>
public sealed class EcTroubleLog
{
    public static readonly TimeSpan DefaultSummaryInterval = TimeSpan.FromMinutes(10);

    private const int RegistersInSummary = 5;

    private readonly IAppLog _log;
    private readonly TimeProvider _time;
    private readonly TimeSpan _summaryInterval;
    private readonly Lock _sync = new();
    private readonly Dictionary<byte, int> _recoveredByRegister = [];

    private DateTimeOffset? _windowStart;
    private int _recoveredCount;
    private int _mostFailedAttempts;

    public EcTroubleLog(IAppLog log, TimeProvider time, TimeSpan summaryInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(summaryInterval, TimeSpan.Zero);
        _log = log;
        _time = time;
        _summaryInterval = summaryInterval;
    }

    public void Report(EcTransactionTrouble trouble)
    {
        if (!trouble.Succeeded || trouble.Operation == EcOperation.Write)
        {
            _log.Warn($"EC: {trouble}");
            return;
        }

        lock (_sync)
        {
            var now = _time.GetUtcNow();
            _windowStart ??= now;
            _recoveredCount++;
            _mostFailedAttempts = Math.Max(_mostFailedAttempts, trouble.Attempts.Count);
            _recoveredByRegister[trouble.Register] = _recoveredByRegister.GetValueOrDefault(trouble.Register) + 1;

            if (now - _windowStart.Value >= _summaryInterval)
            {
                WriteSummary(now);
            }
        }
    }

    /// <summary>Writes the pending summary, if any (call before the EC session closes).</summary>
    public void Flush()
    {
        lock (_sync)
        {
            if (_recoveredCount > 0)
            {
                WriteSummary(_time.GetUtcNow());
            }
        }
    }

    private void WriteSummary(DateTimeOffset now)
    {
        var minutes = (int)(now - _windowStart!.Value).TotalMinutes;
        var busiest = _recoveredByRegister
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(RegistersInSummary)
            .Select(pair => $"0x{pair.Key:X2}×{pair.Value}");
        var more = _recoveredByRegister.Count > RegistersInSummary ? ", …" : string.Empty;

        _log.Info(
            $"EC: son {minutes} dk'da {_recoveredCount} okuma yeniden denemeyle kurtarıldı " +
            $"(en çok {_mostFailedAttempts} başarısız deneme; {string.Join(", ", busiest)}{more}).");

        _windowStart = now;
        _recoveredCount = 0;
        _mostFailedAttempts = 0;
        _recoveredByRegister.Clear();
    }
}
