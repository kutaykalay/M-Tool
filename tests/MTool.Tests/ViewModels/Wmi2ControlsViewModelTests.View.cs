using MTool.App.Hardware;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Sensors;
using MTool.Core.Settings;

namespace MTool.Tests.ViewModels;

/// <summary>What the WMI2 window binds to: which buttons show, the band, the choices and the charge limit draft.</summary>
public sealed partial class Wmi2ControlsViewModelTests
{
    [Fact]
    public async Task Offered_shows_the_turn_on_button_and_the_reading_band()
    {
        var controls = await ReadingAsync();

        controls.ShowEnable.Should().BeTrue();
        controls.IsOpen.Should().BeFalse();
        controls.ShowTurnOff.Should().BeFalse();
        controls.Band.Should().Be(Strings.Wmi2_ExperimentalBand);
    }

    [Fact]
    public async Task Open_shows_the_switches_the_turn_off_button_and_the_control_band()
    {
        var controls = await OpenAsync();

        controls.IsOpen.Should().BeTrue();
        controls.ShowEnable.Should().BeFalse();
        controls.ShowTurnOff.Should().BeTrue();
        controls.Band.Should().Be(Strings.Wmi2_ControlBand);
    }

    [Fact]
    public async Task Unavailable_offers_both_trying_again_and_turning_off()
    {
        _opening = () => Wmi2ControlOpening.NotOpened(Wmi2ControlUnavailable.FirmwareUnreadable);
        var controls = await ReadingAsync();
        var enabling = controls.EnableCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => enabling.IsCompleted);
        await enabling;

        controls.State.Should().Be(Wmi2ControlState.Unavailable);
        controls.ShowEnable.Should().BeTrue();
        controls.ShowTurnOff.Should().BeTrue();
        controls.IsOpen.Should().BeFalse();
        controls.Band.Should().Be(Strings.Wmi2_ExperimentalBand);
    }

    [Fact]
    public async Task Opening_says_so()
    {
        _consent.Current = new Wmi2ExperimentalConsent(Exact, DateTimeOffset.UnixEpoch);

        var controls = await ReadingAsync();

        controls.State.Should().Be(Wmi2ControlState.Opening);
        controls.Reason.Should().Be(Strings.Wmi2_ControlOpening);
        controls.ShowEnable.Should().BeFalse();
        controls.ShowTurnOff.Should().BeFalse();
        PassMinIntervalUntil(() => controls.State == Wmi2ControlState.Open);
        controls.Reason.Should().BeNull();
    }

    [Fact]
    public void The_choices_are_the_three_standard_modes_and_the_two_writable_fan_modes()
    {
        var controls = Create();

        controls.PerformanceOptions.Select(o => o.Mode).Should().Equal(PerformanceMode.High, PerformanceMode.Balanced, PerformanceMode.Eco);
        controls.PerformanceOptions.Select(o => o.Name).Should().Equal(Strings.Mode_High, Strings.Mode_Balanced, Strings.Mode_Eco);
        controls.FanModeOptions.Select(o => o.Mode).Should().Equal(Wmi2FanMode.Auto, Wmi2FanMode.Silent);
        controls.FanModeOptions.Select(o => o.Name).Should().Equal(Strings.Wmi2_FanAuto, Strings.Wmi2_FanSilent);
        (controls.ChargeLimitMin, controls.ChargeLimitMax).Should().Be((50, 100));
    }

    [Fact]
    public async Task The_charge_limit_draft_starts_at_the_current_limit()
    {
        var controls = await ReadingAsync(); // the fake reads 0xD0: 80 %

        controls.ChargeLimitDraft.Should().Be(80);
        controls.ChargeLimitDraftLabel.Should().Be(string.Format(Strings.Format_Percent, 80));
    }

    [Theory]
    [InlineData(0x9E, 50)] // 30 %, set by MSI Center: a new limit starts at the lowest one M-Tool sets
    [InlineData(0x00, 100)] // no limit
    public async Task A_current_limit_M_Tool_cannot_set_starts_the_draft_at_the_nearest_one(byte raw, int draft)
    {
        _readOnly.Status = _readOnly.Status with { ChargeRaw = raw };

        var controls = await ReadingAsync();

        controls.ChargeLimitDraft.Should().Be(draft);
    }

    [Fact]
    public async Task A_moved_draft_stays_while_the_current_limit_stays()
    {
        var controls = await ReadingAsync();
        controls.ChargeLimitDraft = 65;

        _time.Advance(Wmi2ReadPolicy.MinInterval);
        await Live.RefreshCommand.ExecuteAsync(null);

        controls.ChargeLimitDraft.Should().Be(65);
    }

    [Fact]
    public async Task A_changed_current_limit_moves_the_draft()
    {
        var controls = await ReadingAsync();
        controls.ChargeLimitDraft = 65;
        _readOnly.Status = _readOnly.Status with { ChargeRaw = 0xDA }; // 90 %

        _time.Advance(Wmi2ReadPolicy.MinInterval);
        await Live.RefreshCommand.ExecuteAsync(null);

        controls.ChargeLimitDraft.Should().Be(90);
    }
}
