using System.Diagnostics;
using System.Text;
using MTool.App.Hardware;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.App.Cli;

/// <summary>
/// <c>--watch [seconds]</c>: read-only, WMI only (PawnIO is never opened, so the port cannot be
/// used). Polls the performance mode, the fan mode and both fan tables every
/// <see cref="PollInterval"/> and records only the bytes that change, with their time: the tool to
/// see whether a mode write elsewhere disturbs a table. Never writes to the EC. Each register is
/// its own EC operation, so the Access_EC lock is released between reads. Ctrl+C stops early, and
/// the report is saved however the watch ends.
/// </summary>
internal static class WatchCommand
{
    public const string Usage =
        "  M-Tool.exe --watch [saniye]         (salt okuma, yalnızca WMI: 0xF2, 0xF4 ve fan tablolarındaki değişimleri kaydeder, varsayılan 60 sn, en çok 600)";

    private const int DefaultSeconds = 60;
    private const int MaxSeconds = 600;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Performance mode, fan mode and both fan tables, in register order.</summary>
    internal static IReadOnlyList<byte> Registers { get; } =
        [.. EcMap.FanTableRegisters.Append(EcMap.PerformanceMode).Append(EcMap.FanMode).Order()];

    public static bool TryParse(string[] args, out int seconds)
    {
        seconds = DefaultSeconds;
        return args switch
        {
            ["--watch"] => true,
            ["--watch", var text] => int.TryParse(text, out seconds) && seconds is > 0 and <= MaxSeconds,
            _ => false,
        };
    }

    public static int Run(FileLog log, int seconds)
    {
        var report = new StringBuilder();
        var clock = Stopwatch.StartNew();
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            Watch(log, seconds, report, clock, stop.Token);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            Console.WriteLine($"Kaydedildi: {CliRunner.SaveDump(report.ToString(), "watch")}");
        }

        return 0;
    }

    private static void Watch(FileLog log, int seconds, StringBuilder report, Stopwatch clock, CancellationToken stop)
    {
        using var session = EcSession.Open(log, EcBackends.WmiOnly);

        void Line(string text)
        {
            var stamped = $"{DateTime.Now:HH:mm:ss.fff} (t+{clock.Elapsed.TotalSeconds:F2}s) {text}";
            report.AppendLine(stamped);
            Console.WriteLine(stamped);
        }

        Line($"İzleme başladı: {seconds} sn, {PollInterval.TotalMilliseconds} ms aralık, salt okuma, yalnızca WMI, " +
             $"{Registers.Count} register.");
        byte[]? previous = null;
        var samples = 0;
        var failures = 0;
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds) && !stop.IsCancellationRequested)
        {
            try
            {
                var current = Registers.Select(r => CliRunner.Wait(session.Worker.RunAsync(ec => ec.Read(r)))).ToArray();
                samples++;
                if (previous is null)
                {
                    Line($"İlk durum: {Describe(current, _ => true)}");
                }
                else if (!current.AsSpan().SequenceEqual(previous))
                {
                    Line($"Değişti: {Describe(current, i => current[i] != previous[i], previous)}");
                }

                previous = current;
            }
            catch (EcAccessException ex)
            {
                failures++;
                Line($"Okunamadı: {ex.Message}");
            }

            stop.WaitHandle.WaitOne(PollInterval);
        }

        Line($"Bitti: {samples} örnek, {failures} okunamayan{(stop.IsCancellationRequested ? " (Ctrl+C)" : "")}.");
    }

    private static string Describe(byte[] current, Func<int, bool> include, byte[]? previous = null) =>
        string.Join(' ', Enumerable.Range(0, current.Length).Where(include).Select(i =>
            previous is null
                ? $"0x{Registers[i]:X2}={current[i]:X2}"
                : $"0x{Registers[i]:X2}:{previous[i]:X2}->{current[i]:X2}"));
}
