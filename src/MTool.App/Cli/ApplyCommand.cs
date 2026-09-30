using MTool.App.Hardware;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.App.Cli;

/// <summary>
/// <c>--apply &lt;what&gt; &lt;value&gt; [--confirm]</c> and <c>--restore [--confirm]</c>.
/// Without <c>--confirm</c> the gateway runs in dry-run mode and nothing is written.
/// </summary>
internal static class ApplyCommand
{
    public const string Usage =
        "  M-Tool.exe --apply charge <50-100> [--confirm]\n" +
        "  M-Tool.exe --apply boost on|off [--confirm]\n" +
        "  M-Tool.exe --apply perf high|balanced|eco [--confirm]\n" +
        "  M-Tool.exe --apply fan default|cool|silent [--confirm]\n" +
        "  M-Tool.exe --apply fanmode auto|advanced [--confirm]\n" +
        "  M-Tool.exe --restore [--confirm]   (fabrika fan tablosu)\n" +
        "  M-Tool.exe --unlock --confirm      (başarısız yazmadan kalan kilidi kaldır)\n" +
        "  --confirm olmadan: dry-run, EC'ye yazılmaz. Komut satırı settings.json'daki dryRun'a bakmaz;\n" +
        "  her canlı yazma --confirm ister.";

    /// <summary>Plan builder that may need the live EC (Cooler Boost keeps the other bits of 0x98).</summary>
    private delegate WritePlan PlanBuilder(P65CurrentState current);

    private sealed record P65CurrentState(byte CoolerBoostRegister);

    public static bool TryParse(string[] args, out Func<EcSession, Task<WriteOutcome>>? run, out bool confirm)
    {
        confirm = args.Contains("--confirm");
        var rest = args.Where(a => a != "--confirm").ToArray();
        PlanBuilder? builder = rest switch
        {
            ["--restore"] => _ => WritePlans.FanCurves(FactoryDefaults.FanCurves, "Default (restore)"),
            ["--apply", "charge", var percent] when int.TryParse(percent, out var p)
                && p is >= EcWriteRules.MinChargeLimitPercent and <= EcWriteRules.MaxChargeLimitPercent =>
                _ => WritePlans.ChargeLimit(p),
            ["--apply", "boost", "on"] => current => WritePlans.CoolerBoost(true, current.CoolerBoostRegister),
            ["--apply", "boost", "off"] => current => WritePlans.CoolerBoost(false, current.CoolerBoostRegister),
            ["--apply", "perf", var mode] when ParsePerformance(mode) is { } m => _ => WritePlans.Performance(m),
            ["--apply", "fan", var name] when ParseProfile(name) is { } profile =>
                _ => WritePlans.FanCurves(profile.Curves, profile.Name),
            ["--apply", "fanmode", "auto"] => _ => WritePlans.Fan(FanMode.Auto),
            ["--apply", "fanmode", "advanced"] => _ => WritePlans.Fan(FanMode.Advanced),
            _ => null,
        };

        var dryRun = !confirm;
        run = builder is null ? null : session => RunAsync(session, builder, dryRun);
        return run is not null;
    }

    private static async Task<WriteOutcome> RunAsync(EcSession session, PlanBuilder builder, bool dryRun)
    {
        var gateway = await session.CreateGatewayAsync(dryRun).ConfigureAwait(false);
        var current = await session.Worker.RunAsync(ec => new P65CurrentState(ec.Read(EcMap.CoolerBoost))).ConfigureAwait(false);
        return await gateway.ApplyAsync(builder(current)).ConfigureAwait(false);
    }

    private static PerformanceMode? ParsePerformance(string value) => value switch
    {
        "high" => PerformanceMode.High,
        "balanced" => PerformanceMode.Balanced,
        "eco" => PerformanceMode.Eco,
        _ => null,
    };

    private static FanProfile? ParseProfile(string value) => value switch
    {
        "default" => FactoryDefaults.Profile,
        "cool" => Presets.Cool,
        "silent" => Presets.Silent,
        _ => null,
    };
}
