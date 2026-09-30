using System.Diagnostics;
using MTool.Core.Ec;

namespace MTool.App.Cli;

/// <summary>
/// Measures how long the EC stays silent after a read failed on every attempt: the same register
/// is read again every <see cref="ProbeInterval"/> until it answers or <see cref="GiveUpAfter"/>
/// passes. Resolution is one probe, which itself runs the full retry protocol. Read-only.
/// </summary>
internal sealed class RecoveryProbe(Stopwatch clock)
{
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(10);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(5);

    private readonly Lock _sync = new();
    private readonly List<TimeSpan> _recoveries = [];
    private readonly List<TimeSpan> _outages = [];
    private int _gaveUp;
    private TimeSpan _lastReadStarted;
    private Failure? _lastFailure;
    private bool _probing;

    /// <summary>True while probing; the probes' own retries are not part of the stress statistics.</summary>
    public bool IsProbing
    {
        get
        {
            lock (_sync)
            {
                return _probing;
            }
        }
    }

    /// <summary>Wraps the worker's registers so the start of each single-register read is known.</summary>
    public IEcRegisters Track(IEcRegisters ec) => new TimedRegisters(ec, this);

    /// <summary>Called for every EC trouble report (EC worker thread); remembers the last failed read.</summary>
    public void Observe(EcTransactionTrouble trouble)
    {
        if (trouble.Succeeded || trouble.Operation != EcOperation.Read)
        {
            return;
        }

        lock (_sync)
        {
            if (!_probing)
            {
                _lastFailure = new Failure(trouble.Register, _lastReadStarted, clock.Elapsed);
            }
        }
    }

    /// <summary>Forgets an unprobed failure so a stale one from an earlier cycle is never probed.</summary>
    public void CycleStarting()
    {
        lock (_sync)
        {
            _lastFailure = null;
        }
    }

    /// <summary>Probes the register of the last failed read; returns a report line, or null if no read failed.</summary>
    public string? MeasureAfterFailure(EcWorker worker)
    {
        Failure failure;
        lock (_sync)
        {
            if (_lastFailure is not { } last)
            {
                return null;
            }

            failure = last;
            _lastFailure = null;
            _probing = true;
        }

        try
        {
            return Probe(worker, failure);
        }
        finally
        {
            lock (_sync)
            {
                _probing = false;
            }
        }
    }

    public string Summary()
    {
        lock (_sync)
        {
            return string.Join(Environment.NewLine,
                $"Toparlanma (başarısız okumanın bitişinden ilk cevaba): {Spread(_recoveries)}",
                $"Cevapsız dönem (başarısız okumanın başından ilk cevaba): {Spread(_outages)}",
                $"{GiveUpAfter.TotalSeconds:F0} sn içinde toparlanmayan: {_gaveUp}");
        }
    }

    private string Probe(EcWorker worker, Failure failure)
    {
        var probes = 0;
        while (clock.Elapsed + ProbeInterval - failure.FailedAt < GiveUpAfter)
        {
            Thread.Sleep(ProbeInterval);
            probes++;
            try
            {
                CliRunner.Wait(worker.RunAsync(ec => ec.Read(failure.Register)));
            }
            catch (EcAccessException)
            {
                // Still silent: counted in "probes" and tried again after the interval.
                continue;
            }

            var answeredAt = clock.Elapsed;
            var recovery = answeredAt - failure.FailedAt;
            var outage = answeredAt - failure.ReadStarted;
            lock (_sync)
            {
                _recoveries.Add(recovery);
                _outages.Add(outage);
            }

            return $"toparlanma: Read 0x{failure.Register:X2} cevap {recovery.TotalMilliseconds:F0} ms sonra geldi " +
                   $"(başarısız okumanın başından {outage.TotalMilliseconds:F0} ms, {probes} yoklama)";
        }

        lock (_sync)
        {
            _gaveUp++;
        }

        return $"toparlanma YOK: Read 0x{failure.Register:X2} {GiveUpAfter.TotalSeconds:F0} sn içinde cevap vermedi ({probes} yoklama)";
    }

    private static string Spread(List<TimeSpan> values)
    {
        if (values.Count == 0)
        {
            return "ölçüm yok";
        }

        var sorted = values.Select(v => v.TotalMilliseconds).Order().ToArray();
        var middle = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        return $"{sorted.Length} ölçüm, min/medyan/maks = {sorted[0]:F0}/{median:F0}/{sorted[^1]:F0} ms";
    }

    private void ReadStarting()
    {
        lock (_sync)
        {
            _lastReadStarted = clock.Elapsed;
        }
    }

    private readonly record struct Failure(byte Register, TimeSpan ReadStarted, TimeSpan FailedAt);

    /// <summary>Reads register by register, like <see cref="EcController.ReadBlock"/>, stamping each start.</summary>
    private sealed class TimedRegisters(IEcRegisters inner, RecoveryProbe probe) : IEcRegisters
    {
        private const int RegisterCount = 256;

        public byte Read(byte register)
        {
            probe.ReadStarting();
            return inner.Read(register);
        }

        public IReadOnlyList<byte> ReadBlock(byte startRegister, int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(startRegister + count, RegisterCount, nameof(count));
            var values = new byte[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = Read((byte)(startRegister + i));
            }

            return Array.AsReadOnly(values);
        }
    }
}
