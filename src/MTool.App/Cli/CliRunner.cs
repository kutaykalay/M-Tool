using System.IO;
using System.Runtime.InteropServices;
using MTool.App.Hardware;
using MTool.Core.Ec;
using MTool.Core.Settings;

namespace MTool.App.Cli;

/// <summary>Command-line mode (plan.md §3): <c>--dump</c>, <c>--stress</c>, <c>--apply</c>, <c>--restore</c>.</summary>
internal static class CliRunner
{
    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitUsage = 2;
    private const int ExitRejected = 3;
    private const int ExitWriteFailed = 4;
    private const int AttachParentProcess = -1;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public static bool IsCliInvocation(string[] args) => args.Length > 0;

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

            if (StressCommand.TryParse(args, out var seconds, out var protocol))
            {
                return StressCommand.Run(log, seconds, protocol);
            }

            if (args is ["--unlock", "--confirm"])
            {
                return Unlock(log);
            }

            if (ApplyCommand.TryParse(args, out var apply, out var confirm))
            {
                return Apply(log, apply!, confirm);
            }

            Console.WriteLine($"Kullanım:{Environment.NewLine}  M-Tool.exe --dump{Environment.NewLine}{StressCommand.Usage}{Environment.NewLine}{ApplyCommand.Usage}");
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

    private static int Dump(FileLog log)
    {
        using var session = EcSession.Open(log);
        var report = Wait(session.Worker.RunAsync(ec =>
            DumpFormatter.Format(new Core.Device.P65Device(ec), ec, session.Controller.RecoveredFailures)));
        Console.WriteLine(report);
        Console.WriteLine($"Kaydedildi: {SaveDump(report)}");
        return ExitOk;
    }

    private static int Apply(FileLog log, Func<EcSession, Task<WriteOutcome>> apply, bool confirm)
    {
        using var session = EcSession.Open(log);
        // Start off the UI thread so no continuation is ever posted back to it.
        var outcome = Wait(Task.Run(() => apply(session)));

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
                DumpFormatter.Format(new Core.Device.P65Device(ec), ec, session.Controller.RecoveredFailures)));
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
