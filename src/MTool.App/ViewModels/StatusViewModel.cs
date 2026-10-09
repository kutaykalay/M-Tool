using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MTool.App.Resources;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.App.ViewModels;

/// <summary>
/// The window's bands: write access, drift from the desired state, and the last result. A success
/// (<see cref="MessageKind.Info"/>) goes away after <see cref="InfoLifetime"/>; warnings and errors
/// stay. Every message change starts a new version, so an old timer never clears a newer message, even
/// one with the same text. The timer only posts; the version is compared on the UI thread.
/// </summary>
public sealed partial class StatusViewModel(INotifier notifier, IUiDispatcher ui, TimeProvider time) : ObservableObject, IDisposable
{
    public static readonly TimeSpan InfoLifetime = TimeSpan.FromSeconds(10);

    private long _version;
    private ITimer? _hideTimer;
    private bool _disposed;

    [ObservableProperty]
    public partial string? AccessBanner { get; private set; }

    [ObservableProperty]
    public partial MessageKind AccessBannerKind { get; private set; }

    [ObservableProperty]
    public partial string? DriftText { get; private set; }

    [ObservableProperty]
    public partial bool ShowReapply { get; private set; }

    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    public partial MessageKind MessageKind { get; private set; }

    public void SetAccess(DeviceAccess access)
    {
        var readOnly = Strings.Status_ReadOnlyNote;
        (AccessBanner, AccessBannerKind) = access switch
        {
            { WriteMode: WriteMode.Enabled } => ((string?)null, MessageKind.None),
            { WriteMode: WriteMode.DryRun } => (Strings.Status_DryRun, MessageKind.Info),
            { Firmware: null } => (string.Format(Strings.Status_FirmwareUnread, readOnly), MessageKind.Warning),
            { ExperimentalRecord: { } record } =>
                (string.Format(Strings.Status_Experimental, access.Firmware.Version, record, readOnly), MessageKind.Warning),
            { Firmware.IsSupported: false, Match: { Kind: MatchKind.Family } match } =>
                (string.Format(Strings.Status_FirmwareFamily, access.Firmware.Version, match.DisplayName, readOnly), MessageKind.Warning),
            { Firmware.IsSupported: false } =>
                (string.Format(Strings.Status_FirmwareUnsupported, access.Firmware.Version, readOnly), MessageKind.Warning),
            _ => (string.Format(Strings.Status_WritesOff, access.LockReason), MessageKind.Warning),
        };
    }

    /// <summary>Hidden in dry-run: there the EC never follows the choices, so drift is expected.</summary>
    public void SetDrift(StateDrift drift, bool dryRun)
    {
        ShowReapply = drift.Any && !dryRun;
        DriftText = ShowReapply ? string.Format(Strings.Status_Drift, DriftParts(drift)) : null;
    }

    public void Report(CommandResult result)
    {
        var outcome = result.Outcome;
        var (text, kind) = outcome.Status switch
        {
            WriteStatus.Applied or WriteStatus.DryRun => (outcome.Message, MessageKind.Info),
            WriteStatus.Rejected => (outcome.Message, MessageKind.Warning),
            WriteStatus.FailedRecovered => (string.Format(Strings.Status_FailedRecovered, outcome.Message), MessageKind.Error),
            WriteStatus.FailedUnrecovered => (string.Format(Strings.Status_FailedUnrecovered, outcome.Message), MessageKind.Error),
            _ => throw new ArgumentOutOfRangeException(nameof(result), outcome.Status, "Unknown write result."),
        };

        if (kind == MessageKind.Error)
        {
            notifier.ShowError(Strings.Status_FailureTitle, text);
        }

        if (result.SaveWarning is { } warning)
        {
            (text, kind) = ($"{text} {warning}", kind == MessageKind.Error ? kind : MessageKind.Warning);
        }

        SetMessage(text, kind);
    }

    public void ShowWarning(string text) => SetMessage(text, MessageKind.Warning);

    /// <summary>Adds to the message instead of replacing it; an error stays an error.</summary>
    public void AddWarning(string text)
    {
        if (Message is null)
        {
            SetMessage(text, MessageKind.Warning);
            return;
        }

        SetMessage($"{Message} {text}", MessageKind == MessageKind.Error ? MessageKind.Error : MessageKind.Warning);
    }

    /// <summary>Clears the message only if it is still this one; a newer message stays.</summary>
    public void ClearMessage(string ifShowing)
    {
        if (Message == ifShowing)
        {
            SetMessage(null, MessageKind.None);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _hideTimer?.Dispose();
        _hideTimer = null;
    }

    private void SetMessage(string? text, MessageKind kind)
    {
        var version = ++_version;
        _hideTimer?.Dispose();
        _hideTimer = null;
        (Message, MessageKind) = (text, kind);

        if (kind == MessageKind.Info && text is not null && !_disposed)
        {
            _hideTimer = time.CreateTimer(_ => ui.Post(() => HideIfCurrent(version)), null, InfoLifetime, Timeout.InfiniteTimeSpan);
        }
    }

    private void HideIfCurrent(long version)
    {
        if (!_disposed && version == _version)
        {
            SetMessage(null, MessageKind.None);
        }
    }

    private static string DriftParts(StateDrift drift) =>
        string.Join(Strings.List_Separator, new[]
        {
            drift.FanTable ? Strings.Status_DriftFanTable : null,
            drift.Performance ? Strings.Status_DriftPerformance : null,
            drift.ChargeLimit ? Strings.Status_DriftChargeLimit : null,
            drift.FanMode ? Strings.Status_DriftFanMode : null,
        }.OfType<string>());
}
