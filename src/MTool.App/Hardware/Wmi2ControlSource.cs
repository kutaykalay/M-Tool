using MTool.App.ViewModels;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Settings;

namespace MTool.App.Hardware;

/// <summary>What experimental control is built from; <paramref name="ConsentedFirmware"/> is the firmware the user consented for.</summary>
/// <param name="IsWmi2Only">Asked right before every write (<see cref="Wmi2ControlSource.IsWmi2Only"/> in the app).</param>
internal sealed record Wmi2ControlOptions(
    string ConsentedFirmware, Wmi2FirmwareList List, PreStateStore PreState, Func<bool> IsWmi2Only, bool DryRun);

/// <summary>Why experimental control was not opened; the window stays read-only and says so (15c-5).</summary>
internal enum Wmi2ControlUnavailable
{
    /// <summary>The probe did not answer WMI2 (a WMI1 class exists, or nothing): nothing was opened.</summary>
    NotWmi2,

    /// <summary>The running firmware could not be read.</summary>
    FirmwareUnreadable,

    /// <summary>The consent is for another firmware (an update since): it has to be given again.</summary>
    OtherFirmware,

    /// <summary>The firmware is not an exact entry of the list: a family match only reads.</summary>
    NotExactFirmware,
}

/// <summary>Control, or why there is none; never both, never neither.</summary>
internal sealed record Wmi2ControlOpening
{
    private Wmi2ControlOpening(IWmi2Control? source, Wmi2ControlUnavailable? unavailable) =>
        (Source, Unavailable) = (source, unavailable);

    /// <summary>The open control; the caller owns and disposes it.</summary>
    public IWmi2Control? Source { get; }

    public Wmi2ControlUnavailable? Unavailable { get; }

    public static Wmi2ControlOpening Opened(IWmi2Control source) => new(source ?? throw new ArgumentNullException(nameof(source)), null);

    public static Wmi2ControlOpening NotOpened(Wmi2ControlUnavailable reason) => new(null, reason);
}

/// <summary>
/// The live WMI2 values plus experimental control (CLAUDE.md, "WMI2 deneysel yazma"; plan asama15 15c-4). Built only
/// after consent, only when the running firmware is the consented one and an exact list entry; otherwise the window
/// keeps the read-only <see cref="Wmi2LiveSource"/>, which has no write member at all. The gateway runs on the live
/// source's worker, so a write and a poll never interleave, and the session's layout check is read on it first.
/// Only one <c>MSI_ACPI</c> session should be open: the window closes the read-only source before opening this.
/// </summary>
internal sealed class Wmi2ControlSource : IWmi2Control
{
    private readonly Wmi2LiveSource _live;

    private Wmi2ControlSource(Wmi2LiveSource live, EcGateway gateway, Wmi2LayoutResult layout) =>
        (_live, Gateway, Layout) = (live, gateway, layout);

    /// <summary>The only writer: <see cref="EcGateway.ApplyWmi2Async"/> and its siblings.</summary>
    public EcGateway Gateway { get; }

    /// <summary>Probes, opens a writable session and builds control; when control does not apply nothing is left open.</summary>
    /// <exception cref="EcAccessException">The interface cannot be opened or read.</exception>
    public static async Task<Wmi2ControlOpening> OpenAsync(
        string consentedFirmware, Wmi2FirmwareList list, string dataDirectory, bool dryRun, Func<bool> accessGate, IAppLog log,
        CancellationToken cancellationToken)
    {
        // Everything that may throw comes first: from OpenForControl on, CreateAsync owns the open session.
        var options = new Wmi2ControlOptions(
            consentedFirmware, list, PreStateStore.ForWmi2(dataDirectory), () => IsWmi2Only(WmiProbe.Detect), dryRun);
        return Wmi2LiveSource.OpenForControl(accessGate, log) is { } live
            ? await CreateAsync(live, options, log, cancellationToken).ConfigureAwait(false)
            : Wmi2ControlOpening.NotOpened(Wmi2ControlUnavailable.NotWmi2);
    }

    /// <summary>True only when the probe answers WMI2 (no <c>MSI_Software</c>, <c>MSI_ACPI</c> present). A probe error propagates; the gateway counts it as false.</summary>
    internal static bool IsWmi2Only(Func<WmiInterface?> detect) => detect() == WmiInterface.Wmi2;

    /// <summary>Takes <paramref name="live"/> over: it is closed unless control is opened, also when this throws.</summary>
    internal static async Task<Wmi2ControlOpening> CreateAsync(
        Wmi2LiveSource live, Wmi2ControlOptions options, IAppLog log, CancellationToken cancellationToken)
    {
        Wmi2ControlOpening opening;
        try
        {
            opening = await BuildAsync(live, options, log, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            live.Dispose();
            throw;
        }

        if (opening.Source is null)
        {
            live.Dispose();
        }

        return opening;
    }

    public Task<string?> ReadFirmwareAsync(CancellationToken cancellationToken) => _live.ReadFirmwareAsync(cancellationToken);

    public Task<Wmi2Status> ReadStatusAsync(CancellationToken cancellationToken) => _live.ReadStatusAsync(cancellationToken);

    public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken) => _live.ReadSensorsAsync(cancellationToken);

    public Wmi2LayoutResult Layout { get; }

    public bool IsWritePathTested => Gateway.IsWmi2WritePathTested;

    public Task<Wmi2WriteOutcome> TestWritePathAsync(CancellationToken cancellationToken) => Gateway.TestWmi2WritePathAsync(cancellationToken);

    public Task<Wmi2WriteOutcome> ApplyAsync(Wmi2Change change, CancellationToken cancellationToken) =>
        Gateway.ApplyWmi2Async(change, cancellationToken);

    public Task<IReadOnlyList<Wmi2WriteOutcome>> RevertAsync(CancellationToken cancellationToken) => Gateway.RevertWmi2Async(cancellationToken);

    /// <summary>Waits for a running read or write to finish (at most the call timeout), then closes the session.</summary>
    public void Dispose() => _live.Dispose();

    private static async Task<Wmi2ControlOpening> BuildAsync(
        Wmi2LiveSource live, Wmi2ControlOptions options, IAppLog log, CancellationToken cancellationToken)
    {
        var version = await live.ReadFirmwareAsync(cancellationToken).ConfigureAwait(false);
        if (Unavailable(version, options) is { } reason)
        {
            log.Warn($"WMI2 control not opened ({reason}): firmware {version ?? "unreadable"}, consent for {options.ConsentedFirmware}.");
            return Wmi2ControlOpening.NotOpened(reason);
        }

        // WMI2 answers no firmware date; the version is what the list and the consent are keyed on.
        var firmware = new FirmwareInfo(version!, Date: "");
        var layout = Wmi2LayoutCheck.Check(await live.ReadStatusAsync(cancellationToken).ConfigureAwait(false));
        if (!layout.Verified)
        {
            log.Warn($"WMI2 layout does not fit ({layout}); every WMI2 write is off for this session.");
        }

        var setup = new Wmi2GatewaySetup(options.List, firmware, Consented: true, layout, options.PreState, options.IsWmi2Only, options.DryRun);
        log.Info($"WMI2 experimental control open for {firmware.Version} (layout {layout}{(options.DryRun ? ", dry-run" : "")}).");
        return Wmi2ControlOpening.Opened(new Wmi2ControlSource(live, new EcGateway(live.Worker, setup, log), layout));
    }

    private static Wmi2ControlUnavailable? Unavailable(string? version, Wmi2ControlOptions options) =>
        version is null ? Wmi2ControlUnavailable.FirmwareUnreadable
        : !string.Equals(version, options.ConsentedFirmware, StringComparison.Ordinal) ? Wmi2ControlUnavailable.OtherFirmware
        : options.List.Match(new FirmwareInfo(version, Date: "")) != MatchKind.Exact ? Wmi2ControlUnavailable.NotExactFirmware
        : null;
}
