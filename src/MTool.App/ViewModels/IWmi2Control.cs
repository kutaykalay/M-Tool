using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.App.ViewModels;

/// <summary>
/// The live WMI2 values plus experimental control, after the user consented (CLAUDE.md, "WMI2 deneysel yazma").
/// Every write goes through the WMI2 gateway, which checks it, takes the "before M-Tool" snapshot and reads it back.
/// </summary>
internal interface IWmi2Control : IWmi2LiveSource
{
    /// <summary>The session's layout check, read when control opened; when it failed, every write of the session is refused.</summary>
    Wmi2LayoutResult Layout { get; }

    /// <summary>The write-path test (K5) passed in this session; until then a real change is refused.</summary>
    bool IsWritePathTested { get; }

    /// <summary>K5: writes the current performance byte again and reads it back.</summary>
    Task<Wmi2WriteOutcome> TestWritePathAsync(CancellationToken cancellationToken);

    Task<Wmi2WriteOutcome> ApplyAsync(Wmi2Change change, CancellationToken cancellationToken);

    /// <summary>"Back to before M-Tool": one outcome per ceiling register, in ceiling order.</summary>
    Task<IReadOnlyList<Wmi2WriteOutcome>> RevertAsync(CancellationToken cancellationToken);
}
