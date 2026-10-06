using Microsoft.Extensions.Time.Testing;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

public class StatusViewModelTests
{
    private static readonly FirmwareInfo Supported = FakeP65Control.SupportedFirmware;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private readonly FakeNotifier _notifier = new();
    private readonly FakeTimeProvider _time = new();

    private StatusViewModel Status(IUiDispatcher? ui = null) => new(_notifier, ui ?? new ImmediateDispatcher(), _time);

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

        status.AccessBanner.Should().Contain("Deneme modu").And.NotContain("EC");
        status.AccessBannerKind.Should().Be(MessageKind.Info);
    }

    [Fact]
    public void Unknown_firmware_is_named_in_the_banner()
    {
        var status = Status();

        status.SetAccess(new DeviceAccess(new FirmwareInfo("16Q4EMS2.108", ""), WriteMode.Locked, "tanınmayan firmware", PortFeaturesAvailable: true));

        status.AccessBanner.Should().Contain("16Q4EMS2.108").And.Contain("desteklenmiyor").And.Contain("yalnızca izleme");
        status.AccessBannerKind.Should().Be(MessageKind.Warning);
    }

    [Fact]
    public void Unreadable_firmware_says_so()
    {
        var status = Status();

        status.SetAccess(new DeviceAccess(null, WriteMode.Locked, "tanınmayan firmware", PortFeaturesAvailable: true));

        status.AccessBanner.Should().Contain("okunamadı").And.Contain("yalnızca izleme");
    }

    [Fact]
    public void Other_locks_show_their_reason()
    {
        var status = Status();

        status.SetAccess(new DeviceAccess(Supported, WriteMode.Locked, "M-Tool öncesi durum yedeği yok ya da geçersiz", PortFeaturesAvailable: true));

        status.AccessBanner.Should().Contain("Ayar değiştirme kapalı").And.Contain("yedeği yok");
        status.AccessBannerKind.Should().Be(MessageKind.Warning);
    }

    // --- drift ---

    [Fact]
    public void Drift_shows_what_differs_and_offers_reapply()
    {
        var status = Status();

        status.SetDrift(new StateDrift(FanTable: true, Performance: true, ChargeLimit: false, FanMode: false), dryRun: false);

        status.ShowReapply.Should().BeTrue();
        status.DriftText.Should().Contain("fan tablosu").And.Contain("performans modu").And.Contain("Yeniden uygula").And.NotContain("EC");
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
        status.Message.Should().Contain("0xF2").And.Contain("--unlock --confirm").And.Contain("fabrika ayarına");
        _notifier.Errors.Should().ContainSingle().Which.Message.Should().Contain("--unlock --confirm");
        _notifier.Errors[0].Title.Should().Be("M-Tool: ayar uygulanamadı");
    }

    [Fact]
    public void An_unrecovered_failure_asks_for_a_restart()
    {
        var status = Status();

        status.Report(Result(WriteStatus.FailedUnrecovered));

        status.Message.Should().Contain("yeniden başlatın").And.NotContain("EC");
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

    [Fact]
    public void An_added_warning_without_a_message_is_shown_alone()
    {
        var status = Status();

        status.AddWarning("Port okunamadı.");

        status.Message.Should().Be("Port okunamadı.");
        status.MessageKind.Should().Be(MessageKind.Warning);
    }

    // --- info lifetime ---

    [Fact]
    public void An_applied_message_disappears_after_ten_seconds()
    {
        var status = Status();
        var changed = new List<string?>();
        status.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        status.Report(Result(WriteStatus.Applied, "Uygulandı ve doğrulandı."));
        _time.Advance(StatusViewModel.InfoLifetime - Tick);
        status.Message.Should().Be("Uygulandı ve doğrulandı.");

        _time.Advance(Tick);

        status.Message.Should().BeNull();
        status.MessageKind.Should().Be(MessageKind.None);
        changed.Count(name => name == nameof(StatusViewModel.Message)).Should().Be(2);
    }

    [Fact]
    public void A_dry_run_message_disappears_after_ten_seconds()
    {
        var status = Status();

        status.Report(Result(WriteStatus.DryRun, "Deneme modu: kontrol edildi, uygulanmadı."));
        _time.Advance(StatusViewModel.InfoLifetime);

        status.Message.Should().BeNull();
    }

    [Theory]
    [InlineData(WriteStatus.Rejected)]
    [InlineData(WriteStatus.FailedRecovered)]
    [InlineData(WriteStatus.FailedUnrecovered)]
    public void Rejections_and_failures_stay(WriteStatus result)
    {
        var status = Status();

        status.Report(Result(result));
        _time.Advance(TimeSpan.FromMinutes(10));

        status.Message.Should().Contain("mesaj");
    }

    [Fact]
    public void A_direct_warning_stays()
    {
        var status = Status();

        status.ShowWarning("settings.json okunamadı");
        _time.Advance(TimeSpan.FromMinutes(10));

        status.Message.Should().Be("settings.json okunamadı");
    }

    [Fact]
    public void A_success_with_a_save_warning_stays()
    {
        var status = Status();

        status.Report(Result(WriteStatus.Applied, "Uygulandı.", saveWarning: "Ayar kaydedilemedi"));
        _time.Advance(TimeSpan.FromMinutes(10));

        status.Message.Should().Contain("Ayar kaydedilemedi");
    }

    [Fact]
    public void A_success_with_an_added_warning_stays()
    {
        var status = Status();

        status.Report(Result(WriteStatus.Applied, "Uygulandı."));
        status.AddWarning("Port okunamadı.");
        _time.Advance(TimeSpan.FromMinutes(10));

        status.Message.Should().Be("Uygulandı. Port okunamadı.");
        status.MessageKind.Should().Be(MessageKind.Warning);
    }

    [Fact]
    public void The_same_success_again_gets_its_own_ten_seconds()
    {
        var status = Status();
        status.Report(Result(WriteStatus.Applied, "Uygulandı ve doğrulandı."));
        _time.Advance(TimeSpan.FromSeconds(6));

        status.Report(Result(WriteStatus.Applied, "Uygulandı ve doğrulandı."));
        _time.Advance(TimeSpan.FromSeconds(4));
        status.Message.Should().Be("Uygulandı ve doğrulandı.");

        _time.Advance(TimeSpan.FromSeconds(6));
        status.Message.Should().BeNull();
    }

    [Fact]
    public void A_newer_rejection_is_not_cleared_by_the_old_timer()
    {
        var status = Status();
        status.Report(Result(WriteStatus.Applied, "Uygulandı."));
        _time.Advance(TimeSpan.FromSeconds(5));

        status.Report(Result(WriteStatus.Rejected, "Dizüstüne ulaşılamadı."));
        _time.Advance(TimeSpan.FromSeconds(5));

        status.Message.Should().Be("Dizüstüne ulaşılamadı.");
    }

    [Fact]
    public void The_message_is_cleared_on_the_ui_thread()
    {
        var ui = new QueuedDispatcher();
        var status = Status(ui);
        status.Report(Result(WriteStatus.Applied, "Uygulandı."));

        _time.Advance(StatusViewModel.InfoLifetime);
        ui.Pending.Should().Be(1);
        status.Message.Should().Be("Uygulandı.");

        ui.RunAll();
        status.Message.Should().BeNull();
    }

    [Fact]
    public void A_queued_clear_leaves_a_message_that_came_after_it()
    {
        var ui = new QueuedDispatcher();
        var status = Status(ui);
        status.Report(Result(WriteStatus.Applied, "Uygulandı."));
        _time.Advance(StatusViewModel.InfoLifetime);

        status.Report(Result(WriteStatus.Rejected, "Dizüstüne ulaşılamadı."));
        ui.RunAll();

        status.Message.Should().Be("Dizüstüne ulaşılamadı.");
    }

    [Fact]
    public void After_dispose_the_timer_posts_nothing()
    {
        var ui = new QueuedDispatcher();
        var status = Status(ui);
        status.Report(Result(WriteStatus.Applied, "Uygulandı."));

        status.Dispose();
        status.Dispose();
        _time.Advance(StatusViewModel.InfoLifetime);

        ui.Pending.Should().Be(0);
        status.Message.Should().Be("Uygulandı.");
    }

    [Fact]
    public void A_success_after_dispose_starts_no_timer()
    {
        var ui = new QueuedDispatcher();
        var status = Status(ui);
        status.Dispose();

        status.Report(Result(WriteStatus.Applied, "Uygulandı."));
        _time.Advance(StatusViewModel.InfoLifetime);

        ui.Pending.Should().Be(0);
    }

    [Fact]
    public void A_clear_posted_before_dispose_does_nothing_after_it()
    {
        var ui = new QueuedDispatcher();
        var status = Status(ui);
        status.Report(Result(WriteStatus.Applied, "Uygulandı."));
        _time.Advance(StatusViewModel.InfoLifetime);

        status.Dispose();
        ui.RunAll();

        status.Message.Should().Be("Uygulandı.");
    }

    [Fact]
    public void The_banners_do_not_expire()
    {
        var status = Status();
        status.SetAccess(new DeviceAccess(Supported, WriteMode.DryRun, null, PortFeaturesAvailable: true));
        status.SetDrift(new StateDrift(true, false, false, false), dryRun: false);

        _time.Advance(TimeSpan.FromMinutes(10));

        status.AccessBanner.Should().Contain("Deneme modu");
        status.DriftText.Should().NotBeNull();
    }

    [Fact]
    public void A_warning_after_a_cleared_success_is_not_cleared_by_the_old_timer()
    {
        var status = Status();
        status.Report(Result(WriteStatus.Applied, "Uygulandı."));
        _time.Advance(TimeSpan.FromSeconds(3));
        status.ClearMessage("Uygulandı.");
        _time.Advance(TimeSpan.FromSeconds(1));

        status.ShowWarning("settings.json okunamadı");
        _time.Advance(TimeSpan.FromSeconds(6));

        status.Message.Should().Be("settings.json okunamadı");
    }
}
