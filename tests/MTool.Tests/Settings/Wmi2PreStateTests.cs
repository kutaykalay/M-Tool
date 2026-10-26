using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Settings;

public sealed class Wmi2PreStateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly FirmwareInfo Firmware = new("17E2IMS1.10E", "d");

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Capture_reads_the_four_ceiling_registers_twice_and_stores_the_first_pass()
    {
        using var sim = SimulatedWmi2Ec.Typical();

        var state = Wmi2PreStateCapture.Read(new Wmi2EcRegisters(sim), Firmware, Now);

        state.Registers.Should().Equal(
            new RegisterValue(0x98, 0x02),
            new RegisterValue(0xD2, 0xC1),
            new RegisterValue(0xD4, 0x0D),
            new RegisterValue(0xD7, 0xD0));
        state.FirmwareVersion.Should().Be("17E2IMS1.10E");
        state.CapturedAt.Should().Be(Now);
        byte[] ceiling = [.. Wmi2WriteCeiling.Registers];
        sim.Reads.Should().Equal([.. ceiling.Concat(ceiling).Select(Wmi2Calls.DataRequest)]);
    }

    [Fact]
    public void Capture_succeeds_while_the_cooler_boost_low_bits_keep_changing()
    {
        using var sim = SimulatedWmi2Ec.Typical();
        sim.CoolerBoostNoise = true;

        var act = () => Wmi2PreStateCapture.Read(new Wmi2EcRegisters(sim), Firmware, Now);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0x98, 0x02, 0x82)]
    [InlineData(0xD2, 0xC1, 0xC4)]
    public void Capture_fails_when_a_masked_value_differs_between_passes(byte register, byte first, byte second)
    {
        using var sim = SimulatedWmi2Ec.Typical();
        var reads = 0;
        sim.ReadAnswer = request =>
        {
            if (request.Sub != register)
            {
                return null;
            }

            return FakeWmi2Methods.Packet(reads++ == 0 ? first : second);
        };

        var act = () => Wmi2PreStateCapture.Read(new Wmi2EcRegisters(sim), Firmware, Now);

        act.Should().Throw<EcAccessException>().WithMessage($"WMI2 backup not taken*0x{register:X2}*");
    }

    [Fact]
    public void The_wmi2_store_and_the_p65_store_use_different_files()
    {
        using var sim = SimulatedWmi2Ec.Typical();
        var wmi2 = PreStateStore.ForWmi2(_folder);
        var p65 = new PreStateStore(_folder);

        wmi2.SaveIfMissing(Wmi2PreStateCapture.Read(new Wmi2EcRegisters(sim), Firmware, Now));

        File.Exists(Path.Combine(_folder, "pre-mtool-state-wmi2.json")).Should().BeTrue();
        File.Exists(Path.Combine(_folder, "pre-mtool-state.json")).Should().BeFalse();
        wmi2.Load().Should().NotBeNull();
        p65.Exists.Should().BeFalse();
        p65.Load().Should().BeNull();

        p65.SaveIfMissing(PreStateCapture.Read(P65Memory.FactorySnapshot(), new FirmwareInfo("16Q4EMS2.107", "d"), Now))
            .Should().BeTrue();
        wmi2.Load()!.FirmwareVersion.Should().Be("17E2IMS1.10E");
    }

    [Fact]
    public void The_file_path_names_the_file_a_message_points_to()
    {
        PreStateStore.ForWmi2(_folder).FilePath.Should().Be(Path.Combine(_folder, "pre-mtool-state-wmi2.json"));
        new PreStateStore(_folder).FilePath.Should().Be(Path.Combine(_folder, "pre-mtool-state.json"));
    }

    [Fact]
    public void A_wmi2_snapshot_is_never_overwritten()
    {
        using var sim = SimulatedWmi2Ec.Typical();
        var store = PreStateStore.ForWmi2(_folder);
        var first = Wmi2PreStateCapture.Read(new Wmi2EcRegisters(sim), Firmware, Now);

        store.SaveIfMissing(first).Should().BeTrue();
        store.SaveIfMissing(first with { CapturedAt = Now.AddDays(1) }).Should().BeFalse();

        store.Load()!.CapturedAt.Should().Be(Now);
    }

    [Fact]
    public void A_wmi2_snapshot_missing_the_charge_limit_does_not_count()
    {
        using var sim = SimulatedWmi2Ec.Typical();
        var state = Wmi2PreStateCapture.Read(new Wmi2EcRegisters(sim), Firmware, Now);
        var store = PreStateStore.ForWmi2(_folder);
        store.SaveIfMissing(state with { Registers = state.Registers.Where(r => r.Register != 0xD7).ToArray() });

        store.Load().Should().BeNull();
    }

    [Fact]
    public void A_wmi2_snapshot_from_other_firmware_does_not_count()
    {
        using var sim = SimulatedWmi2Ec.Typical();
        var store = PreStateStore.ForWmi2(_folder);
        store.SaveIfMissing(Wmi2PreStateCapture.Read(new Wmi2EcRegisters(sim), Firmware, Now));

        store.HasValidSnapshot("17E2IMS1.10E").Should().BeTrue();
        store.HasValidSnapshot("17E2IMS1.10F").Should().BeFalse();
    }
}
