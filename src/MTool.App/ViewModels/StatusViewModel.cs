using CommunityToolkit.Mvvm.ComponentModel;
using MTool.Core.Device;
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

    private const string FailureTitle = "M-Tool: ayar uygulanamadı";
    private const string ReadOnlyNote = "M-Tool yalnızca izleme modunda, ayar değiştiremez.";

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
        (AccessBanner, AccessBannerKind) = access switch
        {
            { WriteMode: WriteMode.Enabled } => ((string?)null, MessageKind.None),
            { WriteMode: WriteMode.DryRun } =>
                ("Deneme modu: seçimler kontrol ediliyor ama dizüstüne uygulanmıyor. Gerçek uygulama için " +
                    "settings.json'da \"dryRun\": false yapın.", MessageKind.Info),
            { Firmware: null } => ($"Firmware sürümü okunamadı. {ReadOnlyNote}", MessageKind.Warning),
            { Firmware.IsSupported: false } =>
                ($"Bu firmware ({access.Firmware.Version}) desteklenmiyor. {ReadOnlyNote}", MessageKind.Warning),
            _ => ($"Ayar değiştirme kapalı, M-Tool yalnızca izleme modunda. Sebep: {access.LockReason}", MessageKind.Warning),
        };
    }

    /// <summary>Hidden in dry-run: there the EC never follows the choices, so drift is expected.</summary>
    public void SetDrift(StateDrift drift, bool dryRun)
    {
        ShowReapply = drift.Any && !dryRun;
        DriftText = ShowReapply ? $"Dizüstündeki ayarlar seçtiklerinizden farklı ({DriftParts(drift)}). Geri getirmek için Yeniden uygula'ya basın." : null;
    }

    public void Report(CommandResult result)
    {
        var outcome = result.Outcome;
        var (text, kind) = outcome.Status switch
        {
            WriteStatus.Applied or WriteStatus.DryRun => (outcome.Message, MessageKind.Info),
            WriteStatus.Rejected => (outcome.Message, MessageKind.Warning),
            WriteStatus.FailedRecovered => ($"{outcome.Message} Güvenlik için fanlar fabrika ayarına alındı ve ayar değiştirme kapatıldı. Yeniden açmak " +
                "için yönetici komut isteminde M-Tool.exe --unlock --confirm çalıştırın, sonra M-Tool'u yeniden başlatın.", MessageKind.Error),
            WriteStatus.FailedUnrecovered => ($"{outcome.Message} Fanların güvenli ayarda olduğu doğrulanamadı; önlem olarak Cooler Boost açılmaya " +
                "çalışıldı. Bilgisayarı yeniden başlatın, fanlar fabrika ayarına döner.", MessageKind.Error),
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

    private static string DriftParts(StateDrift drift) => string.Join(", ", new[]
    {
        drift.FanTable ? "fan tablosu" : null,
        drift.Performance ? "performans modu" : null,
        drift.ChargeLimit ? "şarj limiti" : null,
        drift.FanMode ? "fan modu" : null,
    }.OfType<string>());
}
