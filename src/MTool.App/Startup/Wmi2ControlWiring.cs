using MTool.App.Hardware;
using MTool.App.ViewModels;
using MTool.Core.Device.Config;

namespace MTool.App.Startup;

/// <summary>What the WMI2 window needs for experimental control: where the consent is kept, how control opens, and the snapshot file it names.</summary>
/// <param name="Open">
/// Opens control for the consented firmware with the window's access gate and the msi-ec list it loaded; it runs off
/// the UI thread.
/// </param>
internal sealed record Wmi2ControlWiring(
    IWmi2Consent Consent, Func<Func<bool>, Wmi2FirmwareList, string, CancellationToken, Task<Wmi2ControlOpening>> Open, string PreStatePath);
