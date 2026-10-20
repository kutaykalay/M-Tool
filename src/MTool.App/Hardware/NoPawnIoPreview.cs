#if DEBUG
namespace MTool.App.Hardware;

/// <summary>
/// Debug builds only: with MTOOL_NO_PAWNIO=1 a session acts as if PawnIO were not installed, so the
/// WMI-only window (<see cref="EcBackends.PreferHybrid"/>) can be checked on a P65 that has it. The
/// port is then never opened; nothing else changes. Release builds have no such type.
/// </summary>
internal static class NoPawnIoPreview
{
    public const string Variable = "MTOOL_NO_PAWNIO";

    public static bool IsOn => Parse(Environment.GetEnvironmentVariable(Variable));

    public static bool Parse(string? value) => value?.Trim() == "1";

    /// <summary>The PawnIO lookup to use: always "missing" while the preview is on, the real one otherwise.</summary>
    public static Func<bool> Installed(bool previewOn, Func<bool> installed) => previewOn ? () => false : installed;
}
#endif
