using MTool.App.Cli;
using MTool.App.Startup;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Cli;

public class CliParsingTests
{
    [Theory]
    [InlineData("--apply", "charge", "60")]
    [InlineData("--apply", "perf", "eco")]
    [InlineData("--apply", "fan", "cool")]
    [InlineData("--apply", "fanmode", "auto")]
    [InlineData("--restore")]
    public void Only_cooler_boost_needs_the_port_read_of_0x98(params string[] args)
    {
        ApplyCommand.TryParse(args, out var request, out _).Should().BeTrue();

        request!.NeedsCoolerBoostRegister.Should().BeFalse();
    }

    [Theory]
    [InlineData("on", 0x02, 0x82)]
    [InlineData("off", 0x82, 0x02)]
    public void Cooler_boost_keeps_the_other_bits_of_the_register_it_read(string state, byte current, byte expected)
    {
        ApplyCommand.TryParse(["--apply", "boost", state, "--confirm"], out var request, out var confirm).Should().BeTrue();

        confirm.Should().BeTrue();
        request!.NeedsCoolerBoostRegister.Should().BeTrue();
        request.Build(current).Writes.Should().Equal(new RegisterWrite(EcMap.CoolerBoost, expected));
    }

    [Fact]
    public void Without_confirm_apply_is_a_dry_run()
    {
        ApplyCommand.TryParse(["--apply", "charge", "60"], out var request, out var confirm).Should().BeTrue();

        confirm.Should().BeFalse();
        request!.Build(0).Writes.Should().Equal(new RegisterWrite(EcMap.ChargeLimit, 0x80 | 60));
    }

    [Theory]
    [InlineData("--apply", "charge", "49")]
    [InlineData("--apply", "boost", "maybe")]
    [InlineData("--stress")]
    public void Unknown_commands_do_not_parse(params string[] args)
    {
        ApplyCommand.TryParse(args, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Watch_reads_only_registers_wmi_reaches()
    {
        WatchCommand.RegistersOf(TestLayouts.P65).Should().OnlyContain(r => WmiMap.Fields.ContainsKey(r))
            .And.Contain([EcMap.PerformanceMode, EcMap.FanMode, EcMap.CpuFan.UpThresholdsStart, EcMap.GpuFan.SpeedsStart])
            .And.NotContain([EcMap.CoolerBoost, EcMap.ChargeLimit]);
    }

    [Theory]
    [InlineData(new[] { "--watch" }, true, 60)]
    [InlineData(new[] { "--watch", "30" }, true, 30)]
    [InlineData(new[] { "--watch", "601" }, false, 601)]
    [InlineData(new[] { "--watch", "0" }, false, 0)]
    public void Watch_takes_an_optional_duration(string[] args, bool parses, int seconds)
    {
        WatchCommand.TryParse(args, out var parsed).Should().Be(parses);
        if (parses)
        {
            parsed.Should().Be(seconds);
        }
    }

    // --- start-up mode ---

    [Fact]
    public void No_arguments_open_the_window()
    {
        StartupArgs.Parse([]).Should().Be(StartupMode.Window);
    }

    [Fact]
    public void Tray_starts_hidden_in_the_tray()
    {
        StartupArgs.Parse(["--tray"]).Should().Be(StartupMode.TrayOnly);
    }

    [Theory]
    [InlineData("--dump")]
    [InlineData("--tray", "--dump")]
    [InlineData("--dump", "--tray")]
    [InlineData("--tray", "--tray")]
    [InlineData("")]
    [InlineData("--TRAY")]
    [InlineData("tray")]
    public void Anything_else_is_the_command_line(params string[] args)
    {
        StartupArgs.Parse(args).Should().Be(StartupMode.CommandLine);
    }
}
