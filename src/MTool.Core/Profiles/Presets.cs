namespace MTool.Core.Profiles;

/// <summary>Ready-made profiles, tested on this laptop with YAMDCC (Kutay's own config, 2026-09-30).</summary>
public static class Presets
{
    /// <summary>Earlier ramp; 100 % at 78 °C (CPU) and 75 °C (GPU). Louder than Default.</summary>
    public static FanProfile Cool { get; } = new("Cool", new FanCurves(
        Cpu: FanCurve.Of((0, 0, 50), (50, 45, 60), (58, 55, 70), (65, 62, 80), (72, 69, 90), (78, 75, 100), (85, 82, 100)),
        Gpu: FanCurve.Of((0, 0, 40), (50, 45, 55), (57, 54, 65), (63, 60, 75), (69, 66, 85), (75, 72, 100), (82, 79, 100))));

    /// <summary>Slow until 60 °C, GPU fan off when idle; still 100 % at 90 °C (CPU) and 88 °C (GPU).</summary>
    public static FanProfile Silent { get; } = new("Silent", new FanCurves(
        Cpu: FanCurve.Of((0, 0, 35), (60, 52, 45), (68, 65, 55), (75, 72, 65), (80, 77, 75), (85, 82, 85), (90, 87, 100)),
        Gpu: FanCurve.Of((0, 0, 0), (60, 52, 40), (67, 64, 50), (73, 70, 60), (78, 75, 70), (83, 80, 85), (88, 85, 100))));
}
