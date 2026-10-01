using CommunityToolkit.Mvvm.ComponentModel;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.App.ViewModels;

/// <summary>The window's bands: write access, drift from the desired state, and the last result.</summary>
public sealed partial class StatusViewModel(INotifier notifier) : ObservableObject
{
    private const string FailureTitle = "M-Tool: EC yazması başarısız";

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
        (AccessBanner, AccessBannerKind) = access switch
        {
            { WriteMode: WriteMode.Enabled } => ((string?)null, MessageKind.None),
            { WriteMode: WriteMode.DryRun } =>
                ("DRY-RUN: seçimler doğrulanıyor ama EC'ye yazılmıyor (settings.json → \"dryRun\": false).", MessageKind.Info),
            { Firmware: null } => ("Firmware okunamadı; salt okunur.", MessageKind.Warning),
            { Firmware.IsSupported: false } =>
                ($"Tanınmayan firmware ({access.Firmware.Version}); salt okunur.", MessageKind.Warning),
            _ => ($"EC yazma kapalı, salt okunur: {access.LockReason}", MessageKind.Warning),
        };
    }

    /// <summary>Hidden in dry-run: there the EC never follows the choices, so drift is expected.</summary>
    public void SetDrift(StateDrift drift, bool dryRun)
    {
        ShowReapply = drift.Any && !dryRun;
        DriftText = ShowReapply ? $"EC istenen durumda değil ({DriftParts(drift)}). Yeniden başlatma ve uyku EC'yi sıfırlar." : null;
    }

    public void Report(CommandResult result)
    {
        var outcome = result.Outcome;
        var (text, kind) = outcome.Status switch
        {
            WriteStatus.Applied or WriteStatus.DryRun => (outcome.Message, MessageKind.Info),
            WriteStatus.Rejected => (outcome.Message, MessageKind.Warning),
            WriteStatus.FailedRecovered => ($"{outcome.Message} Fanlar fabrika eğrisinde, EC yazma kilitlendi. Kilidi " +
                "kaldırmak için yönetici olarak: M-Tool.exe --unlock --confirm, sonra M-Tool'u yeniden başlatın.", MessageKind.Error),
            WriteStatus.FailedUnrecovered => ($"{outcome.Message} Güvenli fan tablosu doğrulanamadı, Cooler Boost denendi. " +
                "Bilgisayarı yeniden başlatın (EC fabrika tablosuna döner).", MessageKind.Error),
            _ => throw new ArgumentOutOfRangeException(nameof(result), outcome.Status, "Bilinmeyen yazma sonucu."),
        };

        if (kind == MessageKind.Error)
        {
            notifier.ShowError(FailureTitle, text);
        }

        if (result.SaveWarning is { } warning)
        {
            (text, kind) = ($"{text} {warning}", kind == MessageKind.Error ? kind : MessageKind.Warning);
        }

        (Message, MessageKind) = (text, kind);
    }

    public void ShowWarning(string text) => (Message, MessageKind) = (text, MessageKind.Warning);

    /// <summary>Adds to the message instead of replacing it; an error stays an error.</summary>
    public void AddWarning(string text) =>
        (Message, MessageKind) = Message is null
            ? (text, MessageKind.Warning)
            : ($"{Message} {text}", MessageKind == MessageKind.Error ? MessageKind.Error : MessageKind.Warning);

    /// <summary>Clears the message only if it is still this one; a newer message stays.</summary>
    public void ClearMessage(string ifShowing)
    {
        if (Message == ifShowing)
        {
            (Message, MessageKind) = (null, MessageKind.None);
        }
    }

    private static string DriftParts(StateDrift drift) => string.Join(", ", new[]
    {
        drift.FanTable ? "fan tablosu" : null,
        drift.Performance ? "performans modu" : null,
        drift.ChargeLimit ? "şarj limiti" : null,
        drift.FanMode ? "fan modu" : null,
    }.OfType<string>());
}
