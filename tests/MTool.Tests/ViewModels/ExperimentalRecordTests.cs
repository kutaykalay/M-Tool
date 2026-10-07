using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

/// <summary>A model read with a draft record (7f): an experimental band, no drift band, implausible tables named.</summary>
public sealed class ExperimentalRecordTests : IDisposable
{
    private static readonly IReadOnlyList<DeviceConfig> Catalog = DeviceConfigLoader.LoadEmbedded(new ListLog());

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();
    private readonly StatusViewModel _status = new(new FakeNotifier(), new ImmediateDispatcher(), TimeProvider.System);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ControlsViewModel Controls()
    {
        var settings = AppSettings.Default with { Desired = new DesiredState("Cool", PerformanceMode.Eco) };
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), settings, new ListLog());
        return new ControlsViewModel(service, _control, _status, new ImmediateDispatcher());
    }

    private static DeviceAccess Draft(string firmware) => new(
        new FirmwareInfo(firmware, ""), WriteMode.Locked, "tanınmayan firmware", PortFeaturesAvailable: false,
        TestLayouts.P65.Capabilities, DeviceMatch.None, ExperimentalRecord: "MSI WMI1 (generic)");

    [Fact]
    public void A_draft_record_shows_an_experimental_band()
    {
        _status.SetAccess(Draft("1541EMS1.115"));

        _status.AccessBanner.Should().Contain("Deneysel").And.Contain("1541EMS1.115").And.Contain("MSI WMI1 (generic)")
            .And.Contain("doğrulanmadı").And.Contain("yalnızca izleme");
        _status.AccessBannerKind.Should().Be(MessageKind.Warning);
    }

    [Fact]
    public void The_control_names_a_draft_layout_and_not_a_verified_one()
    {
        using var worker = new EcWorker(P65Memory.FactorySnapshot(), new FakeEcLock(), TimeSpan.FromMilliseconds(50));
        var gateway = new EcGateway(worker, new WritePolicy(false, true, false, false), new ListLog());
        var setup = new WriteAccessSetup(new FirmwareInfo("16P5EMS1.103", ""), gateway);
        var legacy = DeviceLayout.From(Catalog.Single(c => c.Id == "msi-wmi1-legacy"));

        new P65Control(worker, setup, legacy, new ListLog()).Access.ExperimentalRecord.Should().Be("MSI GE63 / GT72 / GP72 (WMI1, older)");
        new P65Control(worker, setup, TestLayouts.P65, new ListLog()).Access.ExperimentalRecord.Should().BeNull();
    }

    [Fact]
    public async Task A_draft_record_shows_no_drift_band()
    {
        _control.Access = Draft("1541EMS1.115");
        var controls = Controls();

        await controls.RefreshAsync(PortUse.None);

        _status.ShowReapply.Should().BeFalse("nothing is ever written with a draft record");
    }

    [Fact]
    public async Task A_p65_locked_after_a_failed_write_still_shows_drift()
    {
        _control.Access = _control.Access with { WriteMode = WriteMode.Locked, LockReason = "yazma başarısız" };
        var controls = Controls();

        await controls.RefreshAsync(PortUse.None);

        _status.ShowReapply.Should().BeTrue("the user must still learn the EC differs from the choice");
    }

    [Theory]
    [InlineData(WriteMode.Enabled)]
    [InlineData(WriteMode.DryRun)]
    public void A_verified_record_never_shows_the_experimental_band(WriteMode mode)
    {
        _status.SetAccess(new DeviceAccess(
            FakeP65Control.SupportedFirmware, mode, null, PortFeaturesAvailable: true, TestLayouts.P65.Capabilities,
            new DeviceMatch(MatchKind.Exact, EmbeddedDevices.P65Id, "MSI P65 Creator 9SE")));

        (_status.AccessBanner ?? "").Should().NotContain("Deneysel");
    }

    [Fact]
    public void A_p65_family_match_keeps_its_family_band()
    {
        _status.SetAccess(new DeviceAccess(
            new FirmwareInfo("16Q4EMS2.108", ""), WriteMode.Locked, "tanınmayan firmware", PortFeaturesAvailable: false,
            TestLayouts.P65.Capabilities, new DeviceMatch(MatchKind.Family, EmbeddedDevices.P65Id, "MSI P65 Creator 9SE")));

        _status.AccessBanner.Should().Contain("ailesinden").And.NotContain("Deneysel");
    }

    [Fact]
    public void A_real_draft_session_has_the_band_and_the_records_capabilities()
    {
        using var worker = new EcWorker(P65Memory.FactorySnapshot(), new FakeEcLock(), TimeSpan.FromMilliseconds(50));
        var gateway = new EcGateway(worker, new WritePolicy(false, true, false, false), new ListLog());
        var legacy = DeviceLayout.From(Catalog.Single(c => c.Id == "msi-wmi1-legacy")).WithoutPortFeatures();
        var firmware = new FirmwareInfo("16P5EMS1.103", "");
        var setup = new WriteAccessSetup(firmware, gateway) with
        {
            Match = new DeviceMatch(MatchKind.Family, "msi-wmi1-legacy", "MSI GE63 / GT72 / GP72 (WMI1, older)"),
        };
        var access = new P65Control(worker, setup, legacy, new ListLog()).Access;

        _status.SetAccess(access);

        access.Capabilities.ChargeLimit.Should().BeFalse();
        access.Capabilities.CoolerBoost.Should().BeFalse();
        _status.AccessBanner.Should().Contain("Deneysel").And.Contain("16P5EMS1.103").And.Contain("GE63");
    }

    [Fact]
    public async Task Open_writes_still_show_drift()
    {
        var controls = Controls();

        await controls.RefreshAsync(PortUse.None);

        _status.ShowReapply.Should().BeTrue("the EC holds the factory table, Cool was chosen");
    }

    [Fact]
    public async Task An_implausible_fan_table_is_named_as_such()
    {
        var nonsense = new FanCurves(FanCurve.Of((0, 45), (90, 50), (40, 60), (40, 70), (200, 75), (82, 80), (88, 80)), FactoryDefaults.FanCurves.Gpu);
        _control.State = _control.State with { FanCurves = nonsense };
        var controls = Controls();

        await controls.RefreshAsync(PortUse.None);

        controls.ActiveProfileLabel.Should().Contain("makul değil");
    }

    [Fact]
    public async Task An_unknown_but_plausible_table_is_just_unrecognised()
    {
        var custom = new FanCurves(FanCurve.Of((0, 40), (50, 50), (60, 60), (70, 70), (75, 75), (80, 80), (85, 90)), FactoryDefaults.FanCurves.Gpu);
        _control.State = _control.State with { FanCurves = custom };
        var controls = Controls();

        await controls.RefreshAsync(PortUse.None);

        controls.ActiveProfileLabel.Should().Be("Tanınmayan fan ayarı");
    }
}
