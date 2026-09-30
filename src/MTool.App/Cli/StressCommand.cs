using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using MTool.App.Hardware;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.App.Cli;

/// <summary>
/// <c>--stress [seconds] [--step-ms n] [--attempts n]</c>: read-only EC load test. Repeats the <c>--dump</c> reads every
/// <see cref="CycleInterval"/> and prints each retry/failure with its time, failure kind, status
/// byte and drained bytes, so bursts can be matched to charger plugging, Fn keys and the like.
/// <c>--step-ms</c>/<c>--attempts</c> override the protocol timing to compare settings on hardware.
/// After a failed cycle, <see cref="RecoveryProbe"/> measures how long the EC stays silent.
/// Never writes to the EC.
/// </summary>
internal static class StressCommand
{
    public const string Usage =
        "  M-Tool.exe --stress [saniye] [--step-ms 1-200] [--attempts 1-20]\n" +
        "                                      (salt okuma stres ölçümü, varsayılan 300 sn, en çok 3600)";

    private const int DefaultSeconds = 300;
    private const int MaxSeconds = 3600;
    private const int MaxStepMs = 200;
    private const int MaxAttempts = 20;
    private static readonly TimeSpan CycleInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(10);

    public static bool TryParse(string[] args, out int seconds, out EcProtocolOptions protocol)
    {
        seconds = DefaultSeconds;
        protocol = EcProtocolOptions.Default;
        if (args is not ["--stress", .. var rest])
        {
            return false;
        }

        if (rest is [var first, ..] && !first.StartsWith("--", StringComparison.Ordinal))
        {
            if (!int.TryParse(first, out seconds) || seconds is <= 0 or > MaxSeconds)
            {
                return false;
            }

            rest = rest[1..];
        }

        for (; rest.Length > 0; rest = rest[2..])
        {
            switch (rest)
            {
                case ["--step-ms", var text, ..] when int.TryParse(text, out var ms) && ms is > 0 and <= MaxStepMs:
                    protocol = protocol with { StepTimeout = TimeSpan.FromMilliseconds(ms) };
                    break;
                case ["--attempts", var text, ..] when int.TryParse(text, out var n) && n is > 0 and <= MaxAttempts:
                    protocol = protocol with { MaxAttempts = n };
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    public static int Run(FileLog log, int seconds, EcProtocolOptions protocol)
    {
        var tally = new Tally();
        var output = new Output();
        var clock = Stopwatch.StartNew();
        var probe = new RecoveryProbe(clock);
        using var session = EcSession.Open(log, protocol, trouble =>
        {
            probe.Observe(trouble);
            if (probe.IsProbing)
            {
                return;
            }

            tally.Add(trouble);
            output.Enqueue($"{Stamp(clock)} {trouble}");
        });

        output.Line($"Stres ölçümü başladı: {seconds} sn, {CycleInterval.TotalMilliseconds} ms aralık, salt okuma, " +
                    $"adım {protocol.EffectiveStepTimeout.TotalMilliseconds} ms, {protocol.MaxAttempts} deneme.");
        var nextSummary = SummaryInterval;
        var stopRequested = 0;
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            Interlocked.Exchange(ref stopRequested, 1);
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            while (clock.Elapsed < TimeSpan.FromSeconds(seconds) && Volatile.Read(ref stopRequested) == 0)
            {
                RunCycle(session, tally, output, clock, probe);
                if (clock.Elapsed >= nextSummary)
                {
                    output.Line($"{Stamp(clock)} ara özet: {tally.Brief()}");
                    nextSummary += SummaryInterval;
                }

                Thread.Sleep(CycleInterval);
            }
        }
        catch (Exception ex)
        {
            output.Flush();
            output.Line($"ÖLÇÜM YARIDA KESİLDİ: {ex.Message}");
            throw;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            output.Flush();
            if (Volatile.Read(ref stopRequested) != 0)
            {
                output.Line("Ctrl+C: ölçüm erken durduruldu.");
            }

            output.Line(tally.Final());
            output.Line(probe.Summary());
            output.Line($"Kaydedildi: {CliRunner.SaveDump(output.Text, "stress")}");
        }

        return 0;
    }

    private static void RunCycle(EcSession session, Tally tally, Output output, Stopwatch clock, RecoveryProbe probe)
    {
        var started = clock.Elapsed;
        probe.CycleStarting();
        try
        {
            CliRunner.Wait(session.Worker.RunAsync(registers =>
            {
                var ec = probe.Track(registers);
                return DumpFormatter.Format(new P65Device(ec), ec, 0);
            }));
            tally.CycleDone(clock.Elapsed - started, failed: false);
            output.Flush();
        }
        catch (EcAccessException ex)
        {
            tally.CycleDone(clock.Elapsed - started, failed: true);
            output.Flush();
            output.Line($"{Stamp(clock)} DÖNGÜ BAŞARISIZ: {ex.Message}");
            var recovery = probe.MeasureAfterFailure(session.Worker);
            output.Flush();
            if (recovery is not null)
            {
                output.Line($"{Stamp(clock)} {recovery}");
            }
        }
    }

    private static string Stamp(Stopwatch clock) => $"{DateTime.Now:HH:mm:ss.fff} (t+{clock.Elapsed.TotalSeconds:F1}s)";

    /// <summary>
    /// Console plus a copy for the saved report. The EC worker thread (holding the EC mutex) only calls
    /// <see cref="Enqueue"/>; the CLI thread prints those lines in order via <see cref="Flush"/>.
    /// </summary>
    private sealed class Output
    {
        private readonly Lock _sync = new();
        private readonly StringBuilder _text = new();
        private readonly ConcurrentQueue<string> _pending = new();

        public string Text
        {
            get
            {
                lock (_sync)
                {
                    return _text.ToString();
                }
            }
        }

        /// <summary>No console I/O: safe to call while the EC mutex is held.</summary>
        public void Enqueue(string line) => _pending.Enqueue(line);

        public void Flush()
        {
            while (_pending.TryDequeue(out var line))
            {
                Line(line);
            }
        }

        public void Line(string line)
        {
            lock (_sync)
            {
                _text.AppendLine(line);
                Console.WriteLine(line);
            }
        }
    }

    private sealed class Tally
    {
        private readonly Lock _sync = new();
        private readonly Dictionary<EcFailureKind, int> _kinds = [];
        private readonly Dictionary<byte, int> _drained = [];
        private int _cycles;
        private int _failedCycles;
        private int _recovered;
        private int _failed;
        private TimeSpan _slowestCycle;

        public void Add(EcTransactionTrouble trouble)
        {
            lock (_sync)
            {
                if (trouble.Succeeded)
                {
                    _recovered++;
                }
                else
                {
                    _failed++;
                }

                foreach (var attempt in trouble.Attempts)
                {
                    _kinds[attempt.Kind] = _kinds.GetValueOrDefault(attempt.Kind) + 1;
                    foreach (var value in attempt.Drained)
                    {
                        _drained[value] = _drained.GetValueOrDefault(value) + 1;
                    }
                }
            }
        }

        public void CycleDone(TimeSpan duration, bool failed)
        {
            lock (_sync)
            {
                _cycles++;
                _failedCycles += failed ? 1 : 0;
                _slowestCycle = duration > _slowestCycle ? duration : _slowestCycle;
            }
        }

        public string Brief()
        {
            lock (_sync)
            {
                return $"döngü {_cycles} (başarısız {_failedCycles}), kurtarılan işlem {_recovered}, " +
                       $"başarısız işlem {_failed}, en yavaş döngü {_slowestCycle.TotalMilliseconds:F0} ms";
            }
        }

        public string Final()
        {
            lock (_sync)
            {
                var kinds = _kinds.Count == 0 ? "yok" : string.Join(", ", _kinds.Select(k => $"{k.Key}={k.Value}"));
                var drained = _drained.Count == 0
                    ? "yok"
                    : string.Join(", ", _drained.OrderByDescending(d => d.Value).Select(d => $"0x{d.Key:X2}×{d.Value}"));
                return string.Join(Environment.NewLine,
                    "=== Sonuç",
                    Brief(),
                    $"Deneme hataları: {kinds}",
                    $"Boşaltılan baytlar (Windows'a ait olabilir): {drained}");
            }
        }
    }
}
