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

    /// <param name="NeedsCoolerBoostRegister">
    /// Cooler Boost keeps the other bits of 0x98, so only its plan reads that register (through the
    /// port) first; every other plan is built without any EC access.
    /// </param>
    /// <param name="Build">Builds the plan from the current 0x98 (ignored unless needed).</param>
    internal sealed record ApplyRequest(bool NeedsCoolerBoostRegister, Func<byte, WritePlan> Build);

    public static bool TryParse(string[] args, out ApplyRequest? request, out bool confirm)
    {
        confirm = args.Contains("--confirm");
        var rest = args.Where(a => a != "--confirm").ToArray();
        request = rest switch
        {
            ["--restore"] => Fixed(WritePlans.FanCurves(FactoryDefaults.FanCurves, "Default (restore)")),
            ["--apply", "charge", var percent] when int.TryParse(percent, out var p)
                && p is >= EcWriteRules.MinChargeLimitPercent and <= EcWriteRules.MaxChargeLimitPercent =>
                Fixed(WritePlans.ChargeLimit(p)),
            ["--apply", "boost", "on"] => new ApplyRequest(true, current => WritePlans.CoolerBoost(true, current)),
            ["--apply", "boost", "off"] => new ApplyRequest(true, current => WritePlans.CoolerBoost(false, current)),
            ["--apply", "perf", var mode] when ParsePerformance(mode) is { } m => Fixed(WritePlans.Performance(m)),
            ["--apply", "fan", var name] when ParseProfile(name) is { } profile => Fixed(WritePlans.FanCurves(profile.Curves, profile.Name)),
            ["--apply", "fanmode", "auto"] => Fixed(WritePlans.Fan(FanMode.Auto)),
            ["--apply", "fanmode", "advanced"] => Fixed(WritePlans.Fan(FanMode.Advanced)),
            _ => null,
        };
        return request is not null;
    }

    /// <summary>
    /// 0x98 is read through the port only when the gateway could write it at all; otherwise the plan
    /// is built from 0 and the gateway refuses it before any EC access (lock or closed port).
    /// </summary>
    public static async Task<WriteOutcome> RunAsync(EcSession session, ApplyRequest request, bool dryRun)
    {
        var gateway = await session.CreateGatewayAsync(dryRun).ConfigureAwait(false);
        var current = request.NeedsCoolerBoostRegister && gateway.IsWriteEnabled && gateway.IsPortAvailable
            ? await session.Worker.RunAsync(ec => ec.Read(EcMap.CoolerBoost)).ConfigureAwait(false)
            : (byte)0;
        return await gateway.ApplyAsync(request.Build(current)).ConfigureAwait(false);
    }

    private static ApplyRequest Fixed(WritePlan plan) => new(false, _ => plan);

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
