#if DEBUG
using MTool.Core.Diagnostics;
using MTool.Core.Ec;

namespace MTool.App.Startup;

/// <summary>
/// Debug builds only: with MTOOL_PROBE_FAIL=1 start-up acts as if WMI never answered and the session
/// never opened, so the reports-only window with "WMI did not answer" can be checked on a P65 (plan
/// 15a-3, E5). <c>noschema</c> also acts as if MSI's WMI schema were not registered, for the MSI Center
/// hint. Nothing is read from or written to the EC. Release builds have no such type.
/// </summary>
internal sealed record ProbeFailurePreview(bool SchemaMissing)
{
    public const string Variable = "MTOOL_PROBE_FAIL";

    private static readonly WmiFailure NotSupported = new(WmiProbeStage.QuerySoftware, 0x8004100C);

    public static ProbeFailurePreview? Current => Parse(Environment.GetEnvironmentVariable(Variable));

    public static ProbeFailurePreview? Parse(string? value) => value?.Trim() switch
    {
        "1" => new ProbeFailurePreview(SchemaMissing: false),
        "noschema" => new ProbeFailurePreview(SchemaMissing: true),
        _ => null,
    };

    /// <param name="budget">Ignored: the answer is immediate.</param>
    public WmiProbeOutcome Probe(TimeSpan? budget) => WmiProbeOutcome.Failed(NotSupported);

    /// <exception cref="EcAccessException">Always.</exception>
    public TSession OpenSession<TSession>() => throw new EcAccessException($"{Variable}: the session is not opened (Debug preview).");

    public MsiSchemaInfo ReadSchema(Func<MsiSchemaInfo> real) => SchemaMissing
        ? new MsiSchemaInfo(MofImagePathState.NotSet, SchemaFileState.Unknown, SchemaSignature.Unknown)
        : real();
}
#endif
