using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

public class EcControllerWriteTests
{
    [Fact]
    public void Write_stores_the_value_without_protocol_violations()
    {
        var ec = new SimulatedEc();
        IEcWritableRegisters controller = new EcController(ec);

        controller.Write(0xEF, 0xD0);

        ec[0xEF].Should().Be(0xD0);
        ec.CompletedWrites.Should().Be(1);
        ec.ProtocolViolations.Should().Be(0);
    }

    [Fact]
    public void Write_waits_for_a_busy_ec()
    {
        var ec = new SimulatedEc { BusyPolls = 6 };
        IEcWritableRegisters controller = new EcController(ec);

        controller.Write(0x72, 45);

        ec[0x72].Should().Be(45);
        ec.ProtocolViolations.Should().Be(0);
    }

    [Fact]
    public void Write_drains_stale_output_before_starting()
    {
        var ec = new SimulatedEc();
        ec.LeaveStaleOutput(0x46);
        IEcWritableRegisters controller = new EcController(ec, new EcProtocolOptions(MaxAttempts: 2, MaxStatusPolls: 20));

        controller.Write(0xF2, 0xC1);

        ec[0xF2].Should().Be(0xC1);
        ec.ProtocolViolations.Should().Be(0);
    }

    [Fact]
    public void Write_throws_when_the_ec_is_hung_and_changes_nothing()
    {
        var ec = new SimulatedEc { Hung = true };
        IEcWritableRegisters controller = new EcController(ec, new EcProtocolOptions(MaxAttempts: 2, MaxStatusPolls: 20));

        var act = () => controller.Write(0xF2, 0xC1);

        act.Should().Throw<EcAccessException>().WithMessage("*0xF2*");
        ec.CompletedWrites.Should().Be(0);
    }

    [Fact]
    public void The_public_controller_type_exposes_no_write_method()
    {
        typeof(EcController).GetMethods().Select(m => m.Name).Should().NotContain("Write");
    }
}
