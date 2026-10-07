using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

/// <summary>Which controls the window shows for a device record (7d).</summary>
public sealed class ControlsCapabilitiesTests : IDisposable
{
    private static readonly DeviceCapabilities P65 = TestLayouts.P65.Capabilities;

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ControlsViewModel Controls(DeviceCapabilities capabilities)
    {
        _control.Access = _control.Access with { Capabilities = capabilities };
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), AppSettings.Default, new ListLog());
        var status = new StatusViewModel(new FakeNotifier(), new ImmediateDispatcher(), TimeProvider.System);
        return new ControlsViewModel(service, _control, status, new ImmediateDispatcher());
    }

    [Fact]
    public void The_P65_shows_every_control_it_showed_before()
    {
        var controls = Controls(P65);

        new
        {
            controls.ShowGpu,
            controls.ShowFanProfiles,
            controls.ShowPerformance,
            controls.ShowCoolerBoost,
            controls.ShowChargeLimit,
            Modes = controls.PerformanceOptions.Select(o => (o.Mode, o.Name)).ToArray(),
        }.Should().BeEquivalentTo(new
        {
            ShowGpu = true,
            ShowFanProfiles = true,
            ShowPerformance = true,
            ShowCoolerBoost = true,
            ShowChargeLimit = true,
            Modes = new[] { (PerformanceMode.High, "Yüksek"), (PerformanceMode.Balanced, "Dengeli"), (PerformanceMode.Eco, "Pil") },
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void A_single_fan_model_hides_the_gpu()
    {
        Controls(P65 with { GpuFan = false }).ShowGpu.Should().BeFalse();
    }

    [Fact]
    public void A_model_without_a_charge_limit_hides_the_slider()
    {
        var controls = Controls(P65 with { ChargeLimit = false });

        controls.ShowChargeLimit.Should().BeFalse();
        controls.ShowCoolerBoost.Should().BeTrue();
    }

    [Fact]
    public void A_model_without_cooler_boost_hides_it()
    {
        Controls(P65 with { CoolerBoost = false }).ShowCoolerBoost.Should().BeFalse();
    }

    [Fact]
    public void A_model_without_fan_tables_hides_the_profiles()
    {
        Controls(P65 with { FanCurve = false }).ShowFanProfiles.Should().BeFalse();
    }

    [Fact]
    public void Performance_buttons_follow_the_record_in_its_order()
    {
        var controls = Controls(P65 with { PerformanceModes = [PerformanceMode.Eco, PerformanceMode.Balanced] });

        controls.PerformanceOptions.Select(o => o.Name).Should().Equal("Pil", "Dengeli");
        controls.ShowPerformance.Should().BeTrue();
    }

    [Fact]
    public void A_model_without_performance_modes_hides_the_section()
    {
        var controls = Controls(P65 with { PerformanceModes = [] });

        controls.PerformanceOptions.Should().BeEmpty();
        controls.ShowPerformance.Should().BeFalse();
    }

    [Fact]
    public async Task A_saved_mode_the_device_lacks_is_not_shown_as_drift()
    {
        _control.Access = _control.Access with { Capabilities = P65 with { PerformanceModes = [PerformanceMode.Balanced] } };
        _control.State = _control.State with { Performance = PerformanceMode.Balanced, PerformanceRaw = 0xC1 };
        var settings = AppSettings.Default with { Desired = new DesiredState(DesiredState.DefaultProfileName, PerformanceMode.High) };
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), settings, new ListLog());
        var status = new StatusViewModel(new FakeNotifier(), new ImmediateDispatcher(), TimeProvider.System);
        var controls = new ControlsViewModel(service, _control, status, new ImmediateDispatcher());

        await controls.RefreshAsync(PortUse.None);

        status.ShowReapply.Should().BeFalse();
    }

    [Fact]
    public async Task A_locked_device_still_shows_its_controls_disabled()
    {
        _control.Access = _control.Access with { WriteMode = WriteMode.Locked, LockReason = "tanınmayan firmware" };
        var controls = Controls(P65);

        await controls.RefreshAsync(PortUse.None);

        controls.CanWrite.Should().BeFalse();
        controls.ShowCoolerBoost.Should().BeTrue();
        controls.ShowChargeLimit.Should().BeTrue();
    }
}
