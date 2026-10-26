#if DEBUG
using MTool.App.Hardware;
using MTool.App.Startup;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Settings;

namespace MTool.Tests.Views;

public class Wmi2ControlPreviewTests
{
    private const string Firmware = "15M1IMS2.105";

    [Theory]
    [InlineData(null, "Works")]
    [InlineData("", "Works")]
    [InlineData("anything else", "Works")]
    [InlineData(" Consented ", "Consented")]
    [InlineData("dry", "DryRun")]
    [InlineData("fail", "Fail")]
    [InlineData("layout", "Layout")]
    [InlineData("none", "Unavailable")]
    public void The_variable_picks_the_kind(string? value, string kind) =>
        Wmi2ControlPreview.Parse(value).Should().Be(Enum.Parse<Wmi2ControlPreviewKind>(kind));

    [Fact]
    public void Only_consented_starts_with_a_consent_for_the_preview_firmware()
    {
        Wmi2ControlPreview.Consent(Wmi2ControlPreviewKind.Works).Current.Should().BeNull();
        Wmi2ControlPreview.Consent(Wmi2ControlPreviewKind.Consented).Current!.Firmware.Should().Be(Firmware);
    }

    [Fact]
    public void The_consent_is_kept_in_memory_only()
    {
        var consent = Wmi2ControlPreview.Consent(Wmi2ControlPreviewKind.Works);

        consent.Save(new Wmi2ExperimentalConsent(Firmware, DateTimeOffset.UnixEpoch));

        consent.Current!.Firmware.Should().Be(Firmware);
    }

    [Fact]
    public async Task Unavailable_opens_no_control()
    {
        var opening = await Wmi2ControlPreview.OpenAsync(Wmi2ControlPreviewKind.Unavailable, CancellationToken.None);

        opening.Source.Should().BeNull();
        opening.Unavailable.Should().Be(Wmi2ControlUnavailable.FirmwareUnreadable);
    }

    [Fact]
    public async Task A_write_changes_what_the_next_status_read_answers()
    {
        using var control = await OpenedAsync(Wmi2ControlPreviewKind.Works);

        var outcomes = new[]
        {
            await control.ApplyAsync(new Wmi2Change.CoolerBoost(true), CancellationToken.None),
            await control.ApplyAsync(new Wmi2Change.Performance(PerformanceMode.High), CancellationToken.None),
            await control.ApplyAsync(new Wmi2Change.FanMode(Wmi2FanMode.Silent), CancellationToken.None),
            await control.ApplyAsync(new Wmi2Change.ChargeLimit(60), CancellationToken.None),
        };

        outcomes.Should().AllSatisfy(o => o.Status.Should().Be(WriteStatus.Applied));
        var status = await control.ReadStatusAsync(CancellationToken.None);
        (status.CoolerBoost, status.Performance, status.FanMode, status.ChargeLimitPercent)
            .Should().Be((true, PerformanceMode.High, Wmi2FanMode.Silent, 60));
        control.Layout.Verified.Should().BeTrue();
    }

    [Fact]
    public async Task The_write_path_test_passes_once()
    {
        using var control = await OpenedAsync(Wmi2ControlPreviewKind.Works);

        control.IsWritePathTested.Should().BeFalse();
        (await control.TestWritePathAsync(CancellationToken.None)).Status.Should().Be(WriteStatus.Applied);
        control.IsWritePathTested.Should().BeTrue();
    }

    [Fact]
    public async Task Dry_run_writes_nothing()
    {
        using var control = await OpenedAsync(Wmi2ControlPreviewKind.DryRun);
        var before = await control.ReadStatusAsync(CancellationToken.None);

        var outcome = await control.ApplyAsync(new Wmi2Change.CoolerBoost(true), CancellationToken.None);

        outcome.Status.Should().Be(WriteStatus.DryRun);
        (await control.ReadStatusAsync(CancellationToken.None)).Should().Be(before);
    }

    [Fact]
    public async Task Fail_answers_a_recovered_failure_and_back_to_before_answers_one_line_per_register()
    {
        using var control = await OpenedAsync(Wmi2ControlPreviewKind.Fail);

        var outcome = await control.ApplyAsync(new Wmi2Change.ChargeLimit(60), CancellationToken.None);
        var reverted = await control.RevertAsync(CancellationToken.None);

        outcome.Status.Should().Be(WriteStatus.FailedRecovered);
        outcome.Problem.Should().Be(Wmi2GatewayProblem.ReadBackDiffers);
        reverted.Select(o => o.Register).Should().Equal(Wmi2WriteCeiling.Registers);
    }

    [Fact]
    public async Task Layout_does_not_fit()
    {
        using var control = await OpenedAsync(Wmi2ControlPreviewKind.Layout);

        control.Layout.Verified.Should().BeFalse();
    }

    private static async Task<IWmi2Control> OpenedAsync(Wmi2ControlPreviewKind kind)
    {
        var opening = await Wmi2ControlPreview.OpenAsync(kind, CancellationToken.None);
        opening.Source.Should().NotBeNull();
        (await opening.Source!.ReadFirmwareAsync(CancellationToken.None)).Should().Be(Firmware);
        return opening.Source;
    }
}
#endif
