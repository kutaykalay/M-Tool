#if DEBUG
using MTool.App.Startup;
using MTool.Core.Ec;

namespace MTool.Tests.Views;

public class Wmi2PreviewTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("yes please")]
    public void The_preview_is_off_unless_the_variable_names_a_kind(string? value) =>
        Wmi2Preview.Parse(value).Should().BeNull();

    [Theory]
    [InlineData("1", "Values")]
    [InlineData(" FAIL ", "Fail")]
    [InlineData("paused", "Paused")]
    [InlineData("none", "Unavailable")]
    public void The_variable_picks_the_kind(string value, string kind) =>
        Wmi2Preview.Parse(value).Should().Be(Enum.Parse<Wmi2PreviewKind>(kind));

    [Fact]
    public void Unavailable_opens_nothing() =>
        Wmi2Preview.Opener(Wmi2PreviewKind.Unavailable)().Should().BeNull();

    [Fact]
    public async Task Values_change_from_one_round_to_the_next_and_include_an_unclear_speed()
    {
        using var source = Wmi2Preview.Opener(Wmi2PreviewKind.Values)()!;

        (await source.ReadFirmwareAsync(CancellationToken.None)).Should().NotBeNullOrEmpty();
        var first = await source.ReadSensorsAsync(CancellationToken.None);
        var second = await source.ReadSensorsAsync(CancellationToken.None);

        second.CpuTempC.Should().NotBe(first.CpuTempC);
        first.Tach.Should().NotBeNull();
        Core.Device.Wmi2Rpm.Resolve(first.Tach!.Value.GpuHigh, first.Tach.Value.GpuLow).Should().BeNull();
    }

    [Fact]
    public async Task Fail_reads_nothing_but_errors()
    {
        using var source = Wmi2Preview.Opener(Wmi2PreviewKind.Fail)()!;

        var read = () => source.ReadStatusAsync(CancellationToken.None);

        (await read.Should().ThrowAsync<EcAccessException>()).Which.IsAccessPaused.Should().BeFalse();
    }

    [Fact]
    public async Task Paused_answers_as_a_closed_gate_would()
    {
        using var source = Wmi2Preview.Opener(Wmi2PreviewKind.Paused)()!;

        var read = () => source.ReadSensorsAsync(CancellationToken.None);

        (await read.Should().ThrowAsync<EcAccessException>()).Which.IsAccessPaused.Should().BeTrue();
    }
}
#endif
