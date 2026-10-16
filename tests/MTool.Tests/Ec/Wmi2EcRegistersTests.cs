using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>EC registers read through WMI2 <c>Get_Data</c>: read-only, and only at the pinned addresses.</summary>
public class Wmi2EcRegistersTests
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public void A_pinned_register_is_one_get_data_call_whose_value_is_byte_1()
    {
        var wmi = new FakeWmi2Methods { [0x68] = 61 };

        var value = new Wmi2EcRegisters(wmi).Read(0x68);

        value.Should().Be(61);
        wmi.Calls.Should().ContainSingle().Which.Should().Be(Wmi2Calls.Live[0]);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xA0)]
    [InlineData(0xEF)]
    [InlineData(0xFF)]
    public void A_register_outside_the_list_is_refused_before_any_call(byte register)
    {
        var wmi = new FakeWmi2Methods();

        var read = () => new Wmi2EcRegisters(wmi).Read(register);

        read.Should().Throw<InvalidOperationException>().WithMessage($"*0x{register:X2}*");
        wmi.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(31, 1)]
    [InlineData(33, 1)]
    [InlineData(0, 1)]
    [InlineData(32, 0)]
    [InlineData(32, 2)]
    public void A_malformed_or_failed_answer_is_an_ec_access_error(int length, byte status)
    {
        var wmi = new FakeWmi2Methods { AnswerHook = _ => FakeWmi2Methods.Packet(61, status, length) };

        var read = () => new Wmi2EcRegisters(wmi).Read(0x68);

        read.Should().Throw<EcAccessException>().WithMessage("*Get_Data(0x68)*");
    }

    [Fact]
    public void No_answer_at_all_is_an_ec_access_error()
    {
        IWmi2Methods nothing = new NullAnswer();

        var read = () => new Wmi2EcRegisters(nothing).Read(0x68);

        read.Should().Throw<EcAccessException>().WithMessage("*answered nothing*");
    }

    [Fact]
    public void An_ec_access_error_from_the_call_is_not_wrapped_again()
    {
        var error = new EcAccessException("WMI2 timed out");
        var wmi = new FakeWmi2Methods { Error = error };

        var read = () => new Wmi2EcRegisters(wmi).Read(0x68);

        read.Should().Throw<EcAccessException>().Which.Should().BeSameAs(error);
    }

    [Fact]
    public void A_failure_inside_a_block_returns_no_part_of_it()
    {
        var wmi = new FakeWmi2Methods
        {
            AnswerHook = r => r.Sub == 0xC9 ? FakeWmi2Methods.Packet(0, status: 0) : null,
        };

        var read = () => new Wmi2EcRegisters(wmi).ReadBlock(0xC8, 2);

        read.Should().Throw<EcAccessException>();
    }

    [Theory]
    [InlineData(0xC8, -1)]
    [InlineData(0xFF, 2)]
    [InlineData(0xC8, 0x39)]
    public void A_block_outside_the_address_space_is_refused_before_any_call(byte start, int count)
    {
        var wmi = new FakeWmi2Methods();

        var read = () => new Wmi2EcRegisters(wmi).ReadBlock(start, count);

        read.Should().Throw<ArgumentOutOfRangeException>();
        wmi.Calls.Should().BeEmpty();
    }

    [Fact]
    public void An_empty_block_makes_no_call()
    {
        var wmi = new FakeWmi2Methods();

        new Wmi2EcRegisters(wmi).ReadBlock(0xC8, 0).Should().BeEmpty();
        wmi.Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_failed_call_is_an_ec_access_error_with_the_cause()
    {
        var wmi = new FakeWmi2Methods { Error = new TimeoutException("hung") };

        var read = () => new Wmi2EcRegisters(wmi).Read(0x68);

        read.Should().Throw<EcAccessException>().WithInnerException<TimeoutException>();
    }

    [Fact]
    public void A_block_is_one_call_per_register_each_checked()
    {
        var wmi = new FakeWmi2Methods { [0xC8] = 0x01, [0xC9] = 0x41 };

        var block = new Wmi2EcRegisters(wmi).ReadBlock(0xC8, 2);

        block.Should().Equal(0x01, 0x41);
        wmi.Calls.Select(c => c.Sub).Should().Equal(0xC8, 0xC9);
    }

    [Fact]
    public void A_block_that_reaches_an_unlisted_register_is_refused_before_any_call()
    {
        var wmi = new FakeWmi2Methods();

        // 0xCB is listed, 0xCC is not: nothing may be read, not even the first byte.
        var read = () => new Wmi2EcRegisters(wmi).ReadBlock(0xCB, 2);

        read.Should().Throw<InvalidOperationException>();
        wmi.Calls.Should().BeEmpty();
    }

    [Fact]
    public void The_adapter_cannot_write()
    {
        typeof(Wmi2EcRegisters).Should().NotImplement<IEcWritableRegisters>();
    }

    [Fact]
    public async Task The_ec_worker_refuses_a_write_through_it()
    {
        var wmi = new FakeWmi2Methods();
        using var worker = new EcWorker(new Wmi2EcRegisters(wmi), new FakeEcLock(), LockTimeout);

        var write = () => worker.RunWriteAsync(registers => { registers.Write(0x98, 0x80); return 0; });

        await write.Should().ThrowAsync<InvalidOperationException>();
        wmi.Calls.Should().BeEmpty();
    }

    /// <summary>A WMI layer that breaks its contract and answers null.</summary>
    private sealed class NullAnswer : IWmi2Methods
    {
        public byte[] Call(Wmi2Request request) => null!;
    }
}
