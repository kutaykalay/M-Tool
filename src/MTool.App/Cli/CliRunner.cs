using System.IO;
using System.Runtime.InteropServices;
using MTool.App.Hardware;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.App.Cli;

/// <summary>
/// Command-line mode (plan.md §3): <c>--dump</c> prints the EC state. <c>--apply</c> and
/// <c>--restore</c> arrive with the write gateway in stage 2.
/// </summary>
internal static class CliRunner
{
    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitUsage = 2;
    private const int AttachParentProcess = -1;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DumpTimeout = TimeSpan.FromSeconds(15);

    public static bool IsCliInvocation(string[] args) => args.Length > 0;

    public static int Run(string[] args)
    {
        AttachConsole(AttachParentProcess);

        if (args is not ["--dump"])
        {
            Console.WriteLine("Kullanim: M-Tool.exe --dump");
            return ExitUsage;
        }

        try
        {
            var report = Dump();
            var path = SaveReport(report, "dump");
            Console.WriteLine(report);
            Console.WriteLine($"Kaydedildi: {path}");
            return ExitOk;
        }
        catch (Exception ex)
        {
            var errorPath = SaveReport($"HATA{Environment.NewLine}{ex}", "error");
            Console.Error.WriteLine($"HATA: {ex.Message}");
            Console.Error.WriteLine($"Ayrinti: {errorPath}");
            return ExitError;
        }
    }

    private static string Dump()
    {
        if (PawnIoInstallation.InstalledVersion() is null)
        {
            throw new EcAccessException($"PawnIO kurulu degil. Kurmak icin: {PawnIoInstallation.InstallCommand}");
        }

        using var ports = PawnIoPortIo.Open();
        using var ecLock = new AccessEcMutex();
        var controller = new EcController(ports);
        using var worker = new EcWorker(controller, ecLock, LockTimeout,
            (message, ex) => Console.Error.WriteLine($"{message}: {ex.Message}"));

        // The worker runs on its own thread, so blocking the UI thread here cannot deadlock.
        return worker.RunAsync(ec => DumpFormatter.Format(new P65Device(ec), ec, controller.RecoveredFailures))
            .WaitAsync(DumpTimeout)
            .GetAwaiter().GetResult();
    }

    private static string SaveReport(string report, string kind)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M-Tool", "dumps");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, report);
        return path;
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
