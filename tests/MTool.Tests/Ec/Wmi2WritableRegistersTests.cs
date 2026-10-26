using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>
/// EC registers a WMI2 experimental write goes through (plan asama15 15c-2): reads are <c>Get_Data</c> at the
/// pinned addresses, writes are one <c>Set_Data</c> inside the ceiling, and nothing else reaches the EC.
/// </summary>
public sealed class Wmi2WritableRegistersTests : IDisposable
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(1);

    private readonly SimulatedWmi2Ec _ec = SimulatedWmi2Ec.Typical();

    public void Dispose() => _ec.Dispose();

    private IEcWritableRegisters Registers() => new Wmi2WritableRegisters(_ec, _ec);

    [Fact]
    public void A_write_inside_the_ceiling_is_one_set_data_call()
    {
        Registers().Write(0xD2, 0xC4);

        _ec.Writes.Should().ContainSingle().Which.Should().Be(Wmi2Writes.Request(0xD2, 0xC4));
        _ec[0xD2].Should().Be(0xC4);
        _ec.Reads.Should().BeEmpty("the read-back is the gateway's, not the adapter's");
    }

    [Fact]
    public void A_read_is_the_listed_get_data_call()
    {
        var value = Registers().Read(0xD2);

        value.Should().Be(0xC1);
        _ec.Reads.Should().ContainSingle().Which.Should().Be(Wmi2Calls.DataRequest(0xD2));
        Wmi2Calls.Allowed.Should().Contain(_ec.Reads[0]);
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public void A_block_is_read_through_the_listed_calls()
    {
        Registers().ReadBlock(0xC8, 4).Should().HaveCount(4);

        _ec.Reads.Should().Equal(Wmi2Calls.DataRequest(0xC8), Wmi2Calls.DataRequest(0xC9), Wmi2Calls.DataRequest(0xCA), Wmi2Calls.DataRequest(0xCB));
    }

    [Theory]
    [InlineData(0x6A)]
    [InlineData(0xA0)]
    [InlineData(0xF2)]
    [InlineData(0xEB)]
    public void A_register_outside_the_ceiling_never_reaches_set_data(byte register)
    {
        var write = () => Registers().Write(register, 0x0F);

        write.Should().Throw<InvalidOperationException>();
        _ec.Writes.Should().BeEmpty();
        _ec.Reads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0xD2, 0x00)]
    [InlineData(0xD4, 0x8D)]
    [InlineData(0xD7, 0x00)]
    public void A_byte_outside_the_ceiling_never_reaches_set_data(byte register, byte value)
    {
        var write = () => Registers().Write(register, value);

        write.Should().Throw<InvalidOperationException>();
        _ec.Writes.Should().BeEmpty();
        _ec[register].Should().NotBe(value);
    }

    [Theory]
    [InlineData(0xA0)]
    [InlineData(0xEB)]
    public void A_register_outside_the_read_list_is_refused_before_any_call(byte register)
    {
        var read = () => Registers().Read(register);

        read.Should().Throw<InvalidOperationException>();
        _ec.Reads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(31, 1)]
    [InlineData(33, 1)]
    [InlineData(0, 1)]
    [InlineData(32, 0)]
    [InlineData(32, 2)]
    public void A_malformed_or_failed_write_answer_is_an_ec_access_error(int length, byte status)
    {
        _ec.WriteAnswer = _ => FakeWmi2Methods.Packet(0, status, length);

        var write = () => Registers().Write(0xD2, 0xC4);

        write.Should().Throw<EcAccessException>().WithMessage("*Set_Data(0xD2)*");
    }

    [Fact]
    public void No_write_answer_at_all_is_an_ec_access_error()
    {
        IEcWritableRegisters registers = new Wmi2WritableRegisters(_ec, new NullAnswer());

        var write = () => registers.Write(0xD2, 0xC4);

        write.Should().Throw<EcAccessException>().WithMessage("*answered nothing*");
    }

    [Fact]
    public void A_failing_write_call_is_an_ec_access_error_with_its_cause()
    {
        var cause = new System.Runtime.InteropServices.COMException("WBEM_E_FAILED");
        _ec.Error = cause;

        var write = () => Registers().Write(0xD2, 0xC4);

        write.Should().Throw<EcAccessException>().WithMessage("*Set_Data(0xD2)*").Which.InnerException.Should().BeSameAs(cause);
    }

    [Fact]
    public void An_ec_access_error_from_the_write_call_passes_through()
    {
        var error = new EcAccessException("WMI2 session closed.");
        _ec.Error = error;

        var write = () => Registers().Write(0xD2, 0xC4);

        write.Should().Throw<EcAccessException>().Which.Should().BeSameAs(error);
    }

    [Fact]
    public void A_write_the_ec_ignores_is_left_for_the_read_back_to_catch()
    {
        _ec.IgnoresWrites = true;
        var registers = Registers();

        registers.Write(0xD2, 0xC4);

        registers.Read(0xD2).Should().Be(0xC1);
    }

    [Fact]
    public void A_write_that_lands_on_the_neighbour_is_left_for_the_read_back_to_catch()
    {
        _ec.WritesNeighbour = true;
        var registers = Registers();

        registers.Write(0xD2, 0xC4);

        registers.Read(0xD2).Should().Be(0xC1);
    }

    [Fact]
    public async Task The_ec_worker_writes_through_it()
    {
        using var worker = new EcWorker(new Wmi2WritableRegisters(_ec, _ec), new FakeEcLock(), LockTimeout);

        await worker.RunWriteAsync(registers => { registers.Write(0x98, 0x82); return 0; });

        _ec.Writes.Should().ContainSingle().Which.Should().Be(Wmi2Writes.Request(0x98, 0x82));
    }

    [Fact]
    public void The_read_only_adapter_still_cannot_write()
    {
        typeof(Wmi2EcRegisters).Should().NotImplement<IEcWritableRegisters>();
        typeof(Wmi2EcRegisters).Should().NotImplement<IWmi2DataWriter>();
    }

    /// <summary>A WMI layer that breaks its contract and answers null.</summary>
    private sealed class NullAnswer : IWmi2DataWriter
    {
        public byte[] SetData(Wmi2WriteRequest request) => null!;
    }
}
