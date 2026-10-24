using Microsoft.Extensions.Time.Testing;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Core.Sensors;
using MTool.Tests.Fakes;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.ViewModels;

/// <summary>
/// The WMI2 main window (plan asama15 15b-4): the msi-ec list match and the family's performance bytes,
/// shown from what the live read already returns, and the MSI software band. Reading rules stay those
/// of <see cref="Wmi2LiveViewModelTests"/> and <see cref="Wmi2LivePollingTests"/>.
/// </summary>
public class Wmi2MainViewModelTests
{
    private static readonly Wmi2FirmwareList List = Wmi2FirmwareList.Load(
        """
        {
          "schemaVersion": 1,
          "sources": ["msi-ec d7fbbd8"],
          "exact": ["15M3EMS1.105", "15M1IMS2.105"],
          "families": ["15M3EMS1.1", "15M1IMS2.1"],
          "familyOverrides": { "15M1IMS2.1": { "eco": "0xC6" } }
        }
        """,
        [P65()]);

    private readonly FakeWmi2LiveSource _source = new();
    private readonly FakeConfirm _confirm = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ListLog _log = new();

    private Wmi2LiveViewModel Create(Wmi2FirmwareList? list = null, WmiInterface? detected = WmiInterface.Wmi2) =>
        new(() => _source, new Wmi2CircuitBreaker(), _confirm, () => true, _time, _log, detected, list);

    private static string Unverified(string name, string raw) => string.Format(Strings.Wmi2_Unverified, name, raw);

    [Theory]
    [InlineData("15M3EMS1.105", nameof(Strings.Wmi2_ListExact))]
    [InlineData("15M3EMS1.199", nameof(Strings.Wmi2_ListFamily))]
    [InlineData("14F1EMS1.112", nameof(Strings.Wmi2_ListNone))]
    public async Task The_match_is_shown_once_the_firmware_is_read(string firmware, string expectedKey)
    {
        _source.Firmware = firmware;
        var live = Create(List);

        await live.ShowCommand.ExecuteAsync(null);

        live.ListMatch.Should().Be(Strings.ResourceManager.GetString(expectedKey, Strings.Culture));
        _source.FirmwareReads.Should().Be(1); // nothing read for the match itself
    }

    [Fact]
    public void Nothing_is_matched_before_the_user_says_yes()
    {
        var live = Create(List);

        live.ListMatch.Should().Be("—");
        _source.Reads.Should().Be(0);
    }

    [Fact]
    public async Task No_firmware_in_the_answer_is_unknown_not_unlisted()
    {
        _source.Firmware = null;
        var live = Create(List);

        await live.ShowCommand.ExecuteAsync(null);

        live.ListMatch.Should().Be("—");
    }

    [Fact]
    public async Task A_timed_round_keeps_the_match_and_the_familys_mode()
    {
        _source.Firmware = "15M1IMS2.105";
        _source.Status = new Wmi2Status(0x00, 0xC6, 0x0D, 0xD0);
        var live = Create(List);
        await live.ShowCommand.ExecuteAsync(null);
        live.SetWindowShown(true);

        _time.Advance(Wmi2LiveViewModel.PollInterval);
        await Task.Yield();

        _source.SensorReads.Should().Be(2);
        _source.FirmwareReads.Should().Be(1);
        live.ListMatch.Should().Be(Strings.Wmi2_ListExact);
        live.PerformanceMode.Should().Be(Unverified(Strings.Mode_Eco, "0xC6"));
        live.Dispose();
    }

    [Fact]
    public async Task A_refresh_held_back_by_the_two_second_rule_is_read_by_the_next_timed_round()
    {
        var live = Create(List);
        await live.ShowCommand.ExecuteAsync(null);
        live.SetWindowShown(true);
        _time.Advance(Wmi2LiveViewModel.PollInterval); // a timed round: sensors only
        await Task.Yield();
        _source.Status = new Wmi2Status(0x00, 0xC4, 0x0D, 0xD0);

        _time.Advance(TimeSpan.FromSeconds(1));
        await live.RefreshCommand.ExecuteAsync(null); // within 2 s of that round: nothing read now
        (_source.StatusReads, _source.SensorReads).Should().Be((1, 2));

        _time.Advance(Wmi2LiveViewModel.PollInterval - TimeSpan.FromSeconds(1)); // the next timed round
        await Task.Yield();

        (_source.StatusReads, _source.SensorReads).Should().Be((2, 3));
        live.PerformanceMode.Should().Be(Unverified(Strings.Mode_High, "0xC4"));

        _time.Advance(Wmi2LiveViewModel.PollInterval); // and only that one
        await Task.Yield();
        (_source.StatusReads, _source.SensorReads).Should().Be((2, 4));
        live.Dispose();
    }

    [Fact]
    public async Task Stop_forgets_a_held_back_refresh()
    {
        var live = Create(List);
        await live.ShowCommand.ExecuteAsync(null);
        live.SetWindowShown(true);
        _time.Advance(TimeSpan.FromSeconds(1));
        await live.RefreshCommand.ExecuteAsync(null);

        live.StopCommand.Execute(null);
        _time.Advance(TimeSpan.FromSeconds(3));
        await live.ShowCommand.ExecuteAsync(null); // a full round, then timed rounds
        _time.Advance(Wmi2LiveViewModel.PollInterval);
        await Task.Yield();

        (_source.StatusReads, _source.SensorReads).Should().Be((2, 3));
        live.Dispose();
    }

    [Fact]
    public async Task Refresh_keeps_resolving_the_familys_byte()
    {
        _source.Firmware = "15M1IMS2.105";
        var live = Create(List);
        await live.ShowCommand.ExecuteAsync(null);

        _source.Status = new Wmi2Status(0x00, 0xC6, 0x0D, 0xD0);
        _time.Advance(TimeSpan.FromSeconds(3));
        await live.RefreshCommand.ExecuteAsync(null);

        _source.FirmwareReads.Should().Be(1);
        live.PerformanceMode.Should().Be(Unverified(Strings.Mode_Eco, "0xC6"));
    }

    [Fact]
    public void A_list_that_did_not_load_says_so_before_anything_is_read()
    {
        var live = Create(list: null);

        live.ListMatch.Should().Be(Strings.Wmi2_ListUnavailable);
        _source.Reads.Should().Be(0);
    }

    [Fact]
    public async Task A_list_that_did_not_load_stays_unavailable_after_the_firmware_is_read()
    {
        _source.Firmware = "15M3EMS1.105";
        var live = Create(list: null);

        await live.ShowCommand.ExecuteAsync(null);

        live.ListMatch.Should().Be(Strings.Wmi2_ListUnavailable);
    }

    [Fact]
    public async Task Stop_forgets_the_match_until_the_firmware_is_read_again()
    {
        _source.Firmware = "15M3EMS1.105";
        var live = Create(List);
        await live.ShowCommand.ExecuteAsync(null);

        live.StopCommand.Execute(null);

        live.ListMatch.Should().Be("—");
    }

    [Fact]
    public async Task Only_a_wmi2_laptop_is_matched()
    {
        _source.Firmware = "15M3EMS1.105";
        var live = Create(List, detected: WmiInterface.Wmi1);

        await live.ShowCommand.ExecuteAsync(null);

        live.ListMatch.Should().Be(Strings.Wmi2_ListNone);
    }

    [Fact]
    public async Task A_family_override_reads_as_its_mode()
    {
        _source.Firmware = "15M1IMS2.105";
        _source.Status = new Wmi2Status(0x00, 0xC6, 0x0D, 0xD0);
        var live = Create(List);

        await live.ShowCommand.ExecuteAsync(null);

        live.PerformanceMode.Should().Be(Unverified(Strings.Mode_Eco, "0xC6"));
    }

    [Fact]
    public async Task Another_familys_override_stays_a_raw_byte()
    {
        _source.Firmware = "15M3EMS1.105";
        _source.Status = new Wmi2Status(0x00, 0xC6, 0x0D, 0xD0);
        var live = Create(List);

        await live.ShowCommand.ExecuteAsync(null);

        live.PerformanceMode.Should().Be("0xC6");
    }

    [Fact]
    public async Task Without_a_list_an_override_stays_a_raw_byte_and_standard_bytes_still_read()
    {
        _source.Firmware = "15M1IMS2.105";
        _source.Status = new Wmi2Status(0x00, 0xC6, 0x0D, 0xD0);
        var live = Create(list: null);
        await live.ShowCommand.ExecuteAsync(null);
        live.PerformanceMode.Should().Be("0xC6");

        _source.Status = new Wmi2Status(0x00, 0xC4, 0x0D, 0xD0);
        _time.Advance(TimeSpan.FromSeconds(3));
        await live.RefreshCommand.ExecuteAsync(null);

        live.PerformanceMode.Should().Be(Unverified(Strings.Mode_High, "0xC4"));
    }

    [Fact]
    public async Task For_passes_the_list_on()
    {
        _source.Firmware = "15M3EMS1.105";
        using var live = Wmi2LiveViewModel.For(WmiInterface.Wmi2, () => _source, _confirm, () => true, _log, List);

        await live!.ShowCommand.ExecuteAsync(null);

        live.ListMatch.Should().Be(Strings.Wmi2_ListExact);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)] // unknown: no band, M-Tool does not guess
    public async Task The_msi_software_band_shows_only_when_it_is_known_to_run(bool? running, bool shown)
    {
        var band = new MsiSoftwareBand(() => new MsiSoftwareInfo(Installed: true, Running: running), _log);

        await band.LoadAsync();

        band.Message.Should().Be(shown ? Strings.Wmi2_MsiSoftwareRunning : null);
    }

    [Fact]
    public void The_band_reads_nothing_until_loaded()
    {
        var reads = 0;

        var band = new MsiSoftwareBand(() =>
        {
            reads++;
            return new MsiSoftwareInfo(true, true);
        }, _log);

        band.Message.Should().BeNull();
        reads.Should().Be(0);
    }

    [Fact]
    public async Task A_failing_software_check_shows_no_band_and_is_logged()
    {
        var band = new MsiSoftwareBand(() => throw new InvalidOperationException("registry gone"), _log);

        await band.LoadAsync();

        band.Message.Should().BeNull();
        _log.Lines.Should().ContainSingle(l => l.StartsWith("WARN", StringComparison.Ordinal) && l.Contains("MSI software", StringComparison.Ordinal));
    }
}
