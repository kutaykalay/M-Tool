using System.IO;
using System.Runtime.InteropServices;
using MTool.App.Hardware;
using MTool.App.Startup;
using MTool.Core.Ec;
using MTool.Core.Settings;

namespace MTool.App.Cli;

/// <summary>
/// Command-line mode: <c>--dump</c>, <c>--watch</c>, <c>--apply</c>, <c>--restore</c>, <c>--unlock</c>.
/// <c>--watch</c> runs WMI only (PawnIO never opened); the others use the hybrid backend.
/// </summary>
internal static class CliRunner
{
    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitUsage = 2;
    private const int ExitRejected = 3;
    private const int ExitWriteFailed = 4;
    private const int AttachParentProcess = -1;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public static int Run(string[] args)
    {
        AttachConsole(AttachParentProcess);
        var log = new FileLog();

        try
        {
            if (args is ["--dump"])
            {
                return Dump(log);
            }

            if (WatchCommand.TryParse(args, out var watchSeconds))
            {
                return WatchCommand.Run(log, watchSeconds);
            }

            if (args is ["--unlock", "--confirm"])
            {
                return Unlock(log);
            }

            if (ApplyCommand.TryParse(args, out var apply, out var confirm))
            {
                return Apply(log, apply!, confirm);
            }

            Console.WriteLine($"Kullanım:{Environment.NewLine}  M-Tool.exe [{StartupArgs.Tray}]   (GUI; --tray ile yalnızca tepside){Environment.NewLine}  M-Tool.exe --dump{Environment.NewLine}{WatchCommand.Usage}{Environment.NewLine}{ApplyCommand.Usage}");
            return ExitUsage;
        }
        catch (Exception ex)
        {
            log.Error($"Komut başarısız: {string.Join(' ', args)}", ex);
            Console.Error.WriteLine($"HATA: {ex.Message}");
            Console.Error.WriteLine($"Ayrıntı: {AppPaths.Logs}");
            return ExitError;
        }
    }

    /// <summary>Hybrid: everything through WMI except 0x98 and 0xEF, which cost one port read each.</summary>
    private static int Dump(FileLog log)
    {
        using var session = EcSession.Open(log, EcBackends.Hybrid);
        var report = Wait(session.Worker.RunAsync(ec =>
            DumpFormatter.Format(new Core.Device.P65Device(ec), ec, session.Controller?.RecoveredFailures ?? 0)));
        Console.WriteLine(report);
        Console.WriteLine($"Kaydedildi: {SaveDump(report)}");
        return ExitOk;
    }

    private static int Apply(FileLog log, ApplyCommand.ApplyRequest apply, bool confirm)
    {
        using var session = EcSession.Open(log, EcBackends.Hybrid);
        // Start off the UI thread so no continuation is ever posted back to it.
        var outcome = Wait(Task.Run(() => ApplyCommand.RunAsync(session, apply, dryRun: !confirm)));

        var report = string.Join(Environment.NewLine,
            $"{(confirm ? "CANLI" : "DRY-RUN")}: {outcome.Status}",
            outcome.Message,
            $"Yazmalar: {string.Join(' ', outcome.Planned)}");
        Console.WriteLine(report);
        Console.WriteLine($"Kaydedildi: {SaveDump(report + Environment.NewLine + DumpAfter(session, log))}");

        return outcome.Status switch
        {
            WriteStatus.Applied or WriteStatus.DryRun => ExitOk,
            WriteStatus.Rejected => ExitRejected,
            _ => ExitWriteFailed,
        };
    }

    /// <summary>Best effort: a dead EC must not mask the write outcome's exit code.</summary>
    private static string DumpAfter(EcSession session, FileLog log)
    {
        try
        {
            return Wait(session.Worker.RunAsync(ec =>
                DumpFormatter.Format(new Core.Device.P65Device(ec), ec, session.Controller?.RecoveredFailures ?? 0)));
        }
        catch (Exception ex)
        {
            log.Error("Yazma sonrası döküm alınamadı", ex);
            return $"Yazma sonrası döküm alınamadı: {ex.Message}";
        }
    }

    private static int Unlock(FileLog log)
    {
        var store = new WriteLockStore(AppPaths.Root);
        if (store.Reason is not { } reason)
        {
            Console.WriteLine("Yazma kilidi yok.");
            return ExitOk;
        }

        store.Clear();
        log.Warn($"Yazma kilidi elle kaldırıldı. Önceki neden: {reason}");
        Console.WriteLine($"Kilit kaldırıldı. Önceki neden: {reason}");
        return ExitOk;
    }

    // Safe to block: callers pass work that never resumes on the UI thread (ConfigureAwait(false) / Task.Run).
    internal static T Wait<T>(Task<T> task) => task.WaitAsync(CommandTimeout).GetAwaiter().GetResult();

    internal static string SaveDump(string report, string prefix = "dump")
    {
        Directory.CreateDirectory(AppPaths.Dumps);
        var path = Path.Combine(AppPaths.Dumps, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, report);
        return path;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
}
