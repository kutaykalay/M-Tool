namespace MTool.Core.Diagnostics;

/// <summary>Whether <c>Services\WmiAcpi</c> names a MOF resource DLL (<c>MofImagePath</c>).</summary>
public enum MofImagePathState
{
    /// <summary>The value is there.</summary>
    Set,

    /// <summary>The key or the value is not there.</summary>
    NotSet,

    /// <summary>The registry could not be read.</summary>
    Unreadable,
}

/// <summary>Whether the file <c>MofImagePath</c> points at is there.</summary>
public enum SchemaFileState
{
    Exists,
    Missing,

    /// <summary>Not asked (no value) or the check failed.</summary>
    Unknown,
}

/// <summary>
/// Who signed the schema DLL. A fact to report: it is never a reason to refuse anything (MSI's own file is unsigned).
/// The signer text is read from a file a local administrator can replace and is not verified (no chain, no trust),
/// so it can be spoofed: it must never gate a write, a firmware check or any trust decision.
/// </summary>
public enum SchemaSignature
{
    MicroStar,
    Unsigned,
    Other,

    /// <summary>No file to look at, or the signature could not be read.</summary>
    Unknown,
}

/// <summary>
/// Whether Windows knows MSI's WMI classes on this laptop. They exist only when <c>MofImagePath</c>
/// points at the MOF resource DLL that MSI Center installs once (it stays after MSI Center is
/// uninstalled). No path is kept here: the report must not carry one. These are reporting facts only: the registry
/// value and the file can be set by a local user, so none of it may gate a write, a firmware check or a trust decision.
/// </summary>
public sealed record MsiSchemaInfo(MofImagePathState MofImagePath, SchemaFileState File, SchemaSignature Signature)
{
    /// <summary>
    /// True only when the registry says the value is not there: then the classes cannot appear and
    /// MSI Center has to be installed once. An unreadable registry says nothing, so it is not "missing".
    /// </summary>
    public bool SchemaMissing => MofImagePath == MofImagePathState.NotSet;

    private const string MicroStarSubjectPart = "Micro-Star";

    /// <param name="signerSubject">The subject of the file's signer, or null when the file has no signature.</param>
    public static SchemaSignature ClassifySignature(string? signerSubject) =>
        signerSubject is null ? SchemaSignature.Unsigned
        : MsiSoftwareInfo.IsMicroStar(signerSubject) ? SchemaSignature.MicroStar
        : SchemaSignature.Other;
}

/// <summary>
/// Whether MSI's own software is on this laptop, by vendor metadata only (no names are guessed):
/// an Uninstall entry whose publisher, or a running program whose file's company, says Micro-Star. Both texts are
/// free text any local user can forge, and a running check can be spoofed the same way, so it never gates a write,
/// a firmware check or a trust decision.
/// </summary>
/// <param name="Installed">Null when it could not be determined.</param>
/// <param name="Running">Null when it could not be determined.</param>
public sealed record MsiSoftwareInfo(bool? Installed, bool? Running)
{
    private const string MicroStarVendor = "Micro-Star";

    /// <summary>The publisher or company text names MSI's maker.</summary>
    public static bool IsMicroStar(string? vendorText) =>
        vendorText is not null && vendorText.Contains(MicroStarVendor, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Folds per-source answers (true = hit, false = read and no hit, null = could not read): one hit
    /// wins, otherwise a miss wins, and only when nothing could be read is the answer unknown. A scan that
    /// was cut off (<paramref name="truncated"/>) cannot claim a miss: without a hit it is unknown.
    /// </summary>
    public static bool? Decide(IEnumerable<bool?> answers, bool truncated = false)
    {
        var anyRead = false;
        foreach (var answer in answers)
        {
            if (answer == true)
            {
                return true;
            }

            anyRead |= answer.HasValue;
        }

        return anyRead && !truncated ? false : null;
    }
}
