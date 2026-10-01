namespace MTool.Core.Profiles;

/// <summary>Ready-made profiles, tested on this laptop with YAMDCC (Kutay's own config, 2026-09-30).</summary>
public static class Presets
{
    /// <summary>Earlier ramp; 100 % at 78 °C (CPU) and 75 °C (GPU). Louder than Default.</summary>
    public static FanProfile Cool { get; } = new("Cool", new FanCurves(
        Cpu: FanCurve.Of((0, 50), (50, 60), (58, 70), (65, 80), (72, 90), (78, 100), (85, 100)),
        Gpu: FanCurve.Of((0, 40), (50, 55), (57, 65), (63, 75), (69, 85), (75, 100), (82, 100))));

    /// <summary>
    /// Slow until 60 °C, GPU fan off when idle; still 100 % at 90 °C (CPU) and 89 °C (GPU). The GPU's
    /// last step was 88 °C, moved up one degree so the factory 5 °C down offset clears the step below.
    /// </summary>
    public static FanProfile Silent { get; } = new("Silent", new FanCurves(
        Cpu: FanCurve.Of((0, 35), (60, 45), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)),
        Gpu: FanCurve.Of((0, 0), (60, 40), (67, 50), (73, 60), (78, 70), (83, 85), (89, 100))));
}
