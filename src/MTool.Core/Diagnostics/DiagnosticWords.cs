namespace MTool.Core.Diagnostics;

/// <summary>
/// The fixed words for the probe and the MSI schema, shared by the report text and the submission
/// JSON so they cannot drift apart. The server accepts exactly these (M-Tool-reports Worker, schema 2).
/// Every member has its own word: a new one fails here, never silently as another member's word.
/// </summary>
public static class DiagnosticWords
{
    public static string Status(WmiProbeStatus status) => status switch
    {
        WmiProbeStatus.Ok => "ok",
        WmiProbeStatus.Failed => "failed",
        WmiProbeStatus.Timeout => "timeout",
        _ => throw Unknown(status),
    };

    public static string Stage(WmiProbeStage stage) => stage switch
    {
        WmiProbeStage.Connect => "connect",
        WmiProbeStage.QuerySoftware => "query_software",
        WmiProbeStage.QueryAcpi => "query_acpi",
        _ => throw Unknown(stage),
    };

    public static string Registration(MofImagePathState state) => state switch
    {
        MofImagePathState.Set => "set",
        MofImagePathState.NotSet => "not_set",
        MofImagePathState.Unreadable => "unreadable",
        _ => throw Unknown(state),
    };

    public static string File(SchemaFileState state) => state switch
    {
        SchemaFileState.Exists => "exists",
        SchemaFileState.Missing => "missing",
        SchemaFileState.Unknown => "unknown",
        _ => throw Unknown(state),
    };

    public static string Signature(SchemaSignature signature) => signature switch
    {
        SchemaSignature.MicroStar => "micro_star",
        SchemaSignature.Unsigned => "unsigned",
        SchemaSignature.Other => "other",
        SchemaSignature.Unknown => "unknown",
        _ => throw Unknown(signature),
    };

    private static ArgumentOutOfRangeException Unknown<T>(T value)
        where T : struct, Enum => new(nameof(value), value, $"No word for {typeof(T).Name}.{value}.");
}
