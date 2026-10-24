using CommunityToolkit.Mvvm.ComponentModel;
using MTool.App.Resources;
using MTool.Core;
using MTool.Core.Diagnostics;
using MTool.Core.Localization;

namespace MTool.App.ViewModels;

/// <summary>
/// The band that warns when MSI's own software runs (plan asama15 K7): it may undo what M-Tool sets,
/// but M-Tool is never blocked by it. Shown only when the software is known to run; "unknown" shows
/// nothing. The check reads the registry and running programs once, off the UI thread; it never calls
/// WMI or touches the EC.
/// </summary>
/// <param name="readSoftware">Blocks for up to a few seconds (<see cref="Hardware.MsiSoftwareProbe.ReadSoftware"/>).</param>
internal sealed partial class MsiSoftwareBand(Func<MsiSoftwareInfo> readSoftware, IAppLog log) : ObservableObject
{
    /// <summary>Null when MSI software is not known to run.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    public async Task LoadAsync()
    {
        try
        {
            var software = await Task.Run(readSoftware);
            Message = software.Running == true ? Strings.Wmi2_MsiSoftwareRunning : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"MSI software check failed; no band shown: {ex.ForLog()}");
        }
    }
}
