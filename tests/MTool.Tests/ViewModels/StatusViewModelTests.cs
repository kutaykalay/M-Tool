using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

public class StatusViewModelTests
{
    private static readonly FirmwareInfo Supported = FakeP65Control.SupportedFirmware;

    private readonly FakeNotifier _notifier = new();

    private StatusViewModel Status() => new(_notifier);

    private static CommandResult Result(WriteStatus status, string message = "mesaj", string? saveWarning = null) =>
        new(new WriteOutcome(status, [], message), saveWarning);

    // --- access banner ---

    [Fact]
    public void Open_writes_show_no_banner()
    {
        var status = Status();

        status.SetAccess(new DeviceAccess(Supported, WriteMode.Enabled, null, PortFeaturesAvailable: true));

        status.AccessBanner.Should().BeNull();
        status.AccessBannerKind.Should().Be(MessageKind.None);
    }

    [Fact]
    public void Dry_run_is_an_info_banner()
    {
        var status = Status();

        status.SetAccess(new DeviceAccess(Supported, WriteMode.DryRun, null, PortFeaturesAvailable: true));

        status.AccessBanner.Should().Contain("DRY-RUN");
        status.AccessBannerKind.Should().Be(MessageKind.Info);
    }

    [Fact]
    public void Unknown_firmware_is_named_in_the_banner()
    {
        var status = Status();

        status.SetAccess(new DeviceAccess(new FirmwareInfo("16Q4EMS2.108", ""), WriteMode.Locked, "tanınmayan firmware", PortFeaturesAvailable: true));

        status.AccessBanner.Should().Contain("16Q4EMS2.108").And.Contain("salt okunur");
        status.AccessBannerKind.Should().Be(MessageKind.Warning);
    }

    [Fact]
    public void Unreadable_firmware_says_so()
    {
        var status = Status();

        status.SetAccess(new DeviceAccess(null, WriteMode.Locked, "tanınmayan firmware", PortFeaturesAvailable: true));

        status.AccessBanner.Should().Contain("okunamadı");
    }

    [Fact]
    public void Other_locks_show_their_reason()
    {
        var status = Status();

        status.SetAccess(new DeviceAccess(Supported, WriteMode.Locked, "M-Tool öncesi durum yedeği yok ya da geçersiz", PortFeaturesAvailable: true));

        status.AccessBanner.Should().Contain("yedeği yok");
        status.AccessBannerKind.Should().Be(MessageKind.Warning);
    }

    // --- drift ---

    [Fact]
    public void Drift_shows_what_differs_and_offers_reapply()
    {
        var status = Status();

        status.SetDrift(new StateDrift(FanTable: true, Performance: true, ChargeLimit: false, FanMode: false), dryRun: false);

        status.ShowReapply.Should().BeTrue();
        status.DriftText.Should().Contain("fan tablosu").And.Contain("performans modu");
    }

    [Fact]
    public void Drift_is_hidden_in_dry_run()
    {
        var status = Status();

        status.SetDrift(new StateDrift(true, true, true, true), dryRun: true);

        status.ShowReapply.Should().BeFalse();
        status.DriftText.Should().BeNull();
    }

    [Fact]
    public void No_drift_hides_the_band()
    {
        var status = Status();
        status.SetDrift(new StateDrift(true, false, false, false), dryRun: false);

        status.SetDrift(new StateDrift(false, false, false, false), dryRun: false);

        status.ShowReapply.Should().BeFalse();
        status.DriftText.Should().BeNull();
    }

    // --- write results ---

    [Fact]
    public void A_rejected_write_is_a_warning_without_a_popup()
    {
        var status = Status();

        status.Report(Result(WriteStatus.Rejected, "EC yazma kapalı: x"));

        status.Message.Should().Be("EC yazma kapalı: x");
        status.MessageKind.Should().Be(MessageKind.Warning);
        _notifier.Errors.Should().BeEmpty();
    }

    [Fact]
    public void An_applied_write_is_an_info()
    {
        var status = Status();

        status.Report(Result(WriteStatus.Applied, "Uygulandı ve doğrulandı."));

        status.MessageKind.Should().Be(MessageKind.Info);
    }

    [Fact]
    public void A_failed_but_recovered_write_explains_how_to_unlock()
    {
        var status = Status();

        status.Report(Result(WriteStatus.FailedRecovered, "0xF2 doğrulanamadı"));

        status.MessageKind.Should().Be(MessageKind.Error);
        status.Message.Should().Contain("0xF2").And.Contain("--unlock --confirm");
        _notifier.Errors.Should().ContainSingle().Which.Message.Should().Contain("--unlock --confirm");
    }

    [Fact]
    public void An_unrecovered_failure_asks_for_a_restart()
    {
        var status = Status();

        status.Report(Result(WriteStatus.FailedUnrecovered));

        status.Message.Should().Contain("yeniden başlatın");
        _notifier.Errors.Should().ContainSingle();
    }

    [Fact]
    public void A_save_warning_is_shown_with_the_result()
    {
        var status = Status();

        status.Report(Result(WriteStatus.Applied, "Uygulandı.", saveWarning: "Ayar kaydedilemedi"));

        status.Message.Should().Contain("Uygulandı.").And.Contain("Ayar kaydedilemedi");
        status.MessageKind.Should().Be(MessageKind.Warning);
    }

    [Fact]
    public void Warnings_can_be_shown_directly()
    {
        var status = Status();

        status.ShowWarning("settings.json okunamadı");

        status.Message.Should().Be("settings.json okunamadı");
        status.MessageKind.Should().Be(MessageKind.Warning);
    }
}
