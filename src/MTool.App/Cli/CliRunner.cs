using System.IO;
using System.Runtime.InteropServices;
using MTool.App.Hardware;
using MTool.App.Startup;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;
using MTool.Core.Settings;

namespace MTool.App.Cli;

/// <summary>
/// Command-line mode: <c>--dump</c>, <c>--watch</c>, <c>--report</c>, <c>--apply</c>, <c>--restore</c>, <c>--unlock</c>.
/// <c>--watch</c> and <c>--report</c> run WMI only (PawnIO never opened); the others use the hybrid backend.
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

            if (ReportCommand.TryParse(args, out var wmi2))
            {
                return Report(log, wmi2);
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

            Console.WriteLine($"Kullanım:{Environment.NewLine}  M-Tool.exe [{StartupArgs.Tray}]   (GUI; --tray ile yalnızca tepside){Environment.NewLine}  M-Tool.exe --dump{Environment.NewLine}  M-Tool.exe --report [--wmi2]   (başka bir MSI modeli için rapor; yalnızca okur; --wmi2: yeni modellerde ham WMI2 paketleri){Environment.NewLine}{WatchCommand.Usage}{Environment.NewLine}{ApplyCommand.Usage}");
            return ExitUsage;
        }
        catch (UnsupportedDeviceException ex)
        {
            // An expected answer about the laptop, not a fault: no stack trace in the log.
            log.Warn($"Unsupported model: {string.Join(' ', args)}. {ex.Message}");
            Console.Error.WriteLine(ex.Message);
            return ExitError;
        }
        catch (Exception ex)
        {
            log.Error($"Command failed: {string.Join(' ', args)}", ex);
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
            DumpFormatter.Format(new Core.Device.P65Device(ec, session.Layout), session.Layout, session.Match, ec, session.Controller?.RecoveredFailures ?? 0, session.PortAvailable)));
        Console.WriteLine(report);
        Console.WriteLine($"Kaydedildi: {SaveDump(report)}");
        return ExitOk;
    }

    /// <summary>
    /// A part the laptop cannot give is written as such and does not fail the command; only saving
    /// the zip can (the error path below reports it).
    /// </summary>
    private static int Report(FileLog log, bool wmi2)
    {
        var at = DateTimeOffset.Now;
        var data = ReportCommand.Collect(new LiveReportSources(log), GuiBootstrapper.AppVersion, at, log.Warn, wmi2);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = DeviceReport.Format(data.Input,
            [Environment.UserName, Environment.MachineName, Environment.UserDomainName, profile, Path.GetFileName(profile)]);
        var path = ReportCommand.Save(AppPaths.Reports, data, text, at);
        Console.WriteLine(text);
        Console.WriteLine($"Kaydedildi: {path}");
        Console.WriteLine("Bu dosyayı GitHub'da \"Device report\" issue'suna ekleyin. Seri numarası sorgulanmadı, kullanıcı ve bilgisayar adı " +
            "report.txt'de gizlendi. dsdt.aml MSI'ın firmware tablosudur ve filtrelenmez. Göndermeden önce report.txt'ye göz atın.");
        log.Info($"Report written: {path}");
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
                DumpFormatter.Format(new Core.Device.P65Device(ec, session.Layout), session.Layout, session.Match, ec, session.Controller?.RecoveredFailures ?? 0, session.PortAvailable)));
        }
        catch (Exception ex)
        {
            log.Error("Could not take the dump after writing", ex);
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
        log.Warn($"Write lock removed by hand. Previous reason: {reason}");
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
