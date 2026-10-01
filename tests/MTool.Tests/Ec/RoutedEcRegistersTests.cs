using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

public sealed class RoutedEcRegistersTests : IDisposable
{
    private static readonly WritePolicy Live = new(FirmwareSupported: true, PreStateSaved: true, DryRun: false);

    // One EC: WMI and the port both reach this memory, as on the laptop.
    private readonly FakeEcRegisters _ec = P65Memory.Faz0Snapshot();
    private readonly FakeWmiFields _wmi;
    private readonly List<byte> _portReads = [];
    private EcWorker? _worker;

    public RoutedEcRegistersTests()
    {
        _wmi = new FakeWmiFields(_ec);
        _ec.ReadHook = register =>
        {
            _portReads.Add(register);
            return null;
        };
    }

    public void Dispose() => _worker?.Dispose();

    private RoutedEcRegisters Hybrid() => new(_wmi, _ec);

    private RoutedEcRegisters WmiOnly() => new(_wmi, port: null);

    private IEnumerable<byte> PortWrites => _ec.Writes.Select(w => w.Register);

    [Fact]
    public void Firmware_sensors_and_fan_tables_never_touch_the_port()
    {
        var device = new P65Device(Hybrid());

        device.ReadFirmware().Version.Should().Be("16Q4EMS2.107");
        device.ReadSensors().CpuTempC.Should().Be(60);
        device.ReadFanCurves().Should().Be(FactoryDefaults.FanCurves);

        _portReads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0x98, 0x02)]
    [InlineData(0xEF, 0xD0)]
    public void Cooler_boost_and_charge_limit_go_to_the_port(byte register, byte expected)
    {
        Hybrid().Read(register).Should().Be(expected);

        _portReads.Should().Equal(register);
        _wmi.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0x7A)]
    [InlineData(0xF3)]
    [InlineData(0x00)]
    public void Unmapped_registers_are_refused_without_any_ec_access(byte register)
    {
        var routed = Hybrid();

        ((Action)(() => routed.Read(register))).Should().Throw<InvalidOperationException>();
        ((Action)(() => ((IEcWritableRegisters)routed).Write(register, 3))).Should().Throw<InvalidOperationException>();
        _portReads.Should().BeEmpty();
        _ec.Writes.Should().BeEmpty();
        _wmi.Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_refused_register_is_not_retried()
    {
        var warnings = new List<string>();
        var reader = new RetryingEcReader(Hybrid(), EcAccessRetry.Default with { Sleep = _ => { } }, warnings.Add);

        ((Action)(() => reader.Read(0x7A))).Should().Throw<InvalidOperationException>();
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Without_a_port_cooler_boost_is_refused_too()
    {
        var routed = WmiOnly();

        ((Action)(() => routed.Read(0x98))).Should().Throw<InvalidOperationException>().WithMessage("*port*");
        ((Action)(() => ((IEcWritableRegisters)routed).Write(0xEF, 0xD0))).Should().Throw<InvalidOperationException>();
        _portReads.Should().BeEmpty();
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public void A_block_is_read_with_one_wmi_call_per_class()
    {
        Hybrid().ReadBlock(0x6A, 6).Should().Equal(55, 64, 70, 76, 82, 88);

        _wmi.Calls.Should().Equal("read MSI_CPU[5,6,7,8,9,10]");
    }

    [Fact]
    public void A_block_across_wmi_and_port_keeps_register_order()
    {
        var routed = Hybrid();
        routed.ReadBlock(0xCA, 4).Should().Equal(0, 0, 0, 157);

        // 0x96 and 0x97 are unmapped, so a block across them fails before any access.
        ((Action)(() => routed.ReadBlock(0x96, 3))).Should().Throw<InvalidOperationException>();

        _wmi.Calls.Should().Equal("read MSI_AP[5,4,3,2]");
    }

    [Theory]
    [InlineData(300)]
    [InlineData(-1)]
    public void A_wmi_value_that_does_not_fit_a_byte_is_an_access_error(int value)
    {
        _wmi.ReadHook = field => field == new WmiField(WmiMap.CpuClass, 1) ? value : null;

        ((Action)(() => Hybrid().Read(0x68))).Should().Throw<EcAccessException>().WithMessage($"*{value}*");
    }

    [Fact]
    public void A_wmi_answer_with_the_wrong_field_count_is_an_access_error()
    {
        var routed = new RoutedEcRegisters(new ShortAnswerWmi(), port: null);

        ((Action)(() => routed.ReadBlock(0x6A, 6))).Should().Throw<EcAccessException>();
    }

    [Fact]
    public void A_silent_wmi_call_is_retried_like_a_silent_ec()
    {
        _wmi.SilentCalls = 2;
        var warnings = new List<string>();
        var reader = new RetryingEcReader(Hybrid(), EcAccessRetry.Default with { Sleep = _ => { } }, warnings.Add);

        reader.Read(0xF2).Should().Be(0xC0);
        warnings.Should().HaveCount(2);
    }

    private sealed class ShortAnswerWmi : IWmiFields
    {
        public IReadOnlyList<int> Read(string className, IReadOnlyList<int> indices) => [0];

        public void Write(string className, int index, byte value) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(0x68)] // CPU temperature
    [InlineData(0xCC)] // RPM
    [InlineData(0xA0)] // firmware text
    public void Read_only_registers_are_never_written_even_though_wmi_reaches_them(byte register)
    {
        IEcWritableRegisters routed = Hybrid();

        ((Action)(() => routed.Write(register, 0))).Should().Throw<InvalidOperationException>();
        _wmi.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Writes_go_to_the_mapped_field_or_the_port()
    {
        IEcWritableRegisters routed = Hybrid();

        routed.Write(0xF2, 0xC1);
        routed.Write(0x72, 50);
        routed.Write(0xEF, 0xD1);

        _wmi.Calls.Should().Equal("write MSI_System[7]=0xC1", "write MSI_CPU[11]=0x32");
        PortWrites.Should().Equal(0xEF);
        _ec[0xF2].Should().Be(0xC1);
        _ec[0xEF].Should().Be(0xD1);
    }

    [Fact]
    public async Task The_gateway_applies_a_fan_profile_through_wmi_alone()
    {
        _worker = new EcWorker(Hybrid(), new FakeEcLock(), TimeSpan.FromMilliseconds(50));
        var gateway = new EcGateway(_worker, Live, new ListLog());

        var outcome = await gateway.ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.Applied);
        new P65Device(Hybrid()).ReadFanCurves().Should().Be(Presets.Cool.Curves);
        _portReads.Should().BeEmpty();
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_wmi_write_that_never_lands_locks_the_gateway_and_restores_the_factory_table()
    {
        _worker = new EcWorker(Hybrid(), new FakeEcLock(), TimeSpan.FromMilliseconds(50));
        var gateway = new EcGateway(_worker, Live, new ListLog(), retry: EcAccessRetry.Default with { Sleep = _ => { } });
        _wmi.StuckFields.Add(new WmiField(WmiMap.CpuClass, 11));

        var outcome = await gateway.ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.FailedRecovered);
        gateway.IsWriteEnabled.Should().BeFalse();
        new P65Device(Hybrid()).ReadFanCurves().Should().Be(FactoryDefaults.FanCurves);
        _portReads.Should().BeEmpty();
    }

    [Fact]
    public async Task The_gateway_writes_the_charge_limit_through_the_port()
    {
        _worker = new EcWorker(Hybrid(), new FakeEcLock(), TimeSpan.FromMilliseconds(50));
        var gateway = new EcGateway(_worker, Live, new ListLog());

        var outcome = await gateway.ApplyAsync(WritePlans.ChargeLimit(79));

        outcome.Status.Should().Be(WriteStatus.Applied);
        PortWrites.Should().Equal(0xEF);
        _wmi.Calls.Should().NotContain(c => c.StartsWith("write"));
    }
}
