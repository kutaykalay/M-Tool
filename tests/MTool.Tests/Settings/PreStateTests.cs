using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Settings;

public sealed class PreStateTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Capture_reads_every_writable_register()
    {
        var ec = P65Memory.Faz0Snapshot();

        var state = PreStateCapture.Read(ec, new FirmwareInfo("16Q4EMS2.107", "05132019"), Now);

        state.Registers.Select(r => r.Register).Should().BeEquivalentTo(EcWriteRules.WritableRegisters);
        state.Registers.Should().Contain(new RegisterValue(0xEF, 0xD0));
        state.Registers.Should().Contain(new RegisterValue(0x72, 45));
        state.FirmwareVersion.Should().Be("16Q4EMS2.107");
        state.CapturedAt.Should().Be(Now);
    }

    [Fact]
    public void Capture_fails_when_two_passes_disagree()
    {
        var ec = new ChangingRegisters(P65Memory.Faz0Snapshot(), changing: 0x72);

        var act = () => PreStateCapture.Read(ec, new FirmwareInfo("16Q4EMS2.107", "05132019"), Now);

        act.Should().Throw<EcAccessException>().WithMessage("*0x72*");
    }

    [Fact]
    public void Store_saves_once_and_never_overwrites()
    {
        var store = new PreStateStore(_folder);
        var first = PreStateCapture.Read(P65Memory.Faz0Snapshot(), new FirmwareInfo("16Q4EMS2.107", "d"), Now);
        var second = first with { CapturedAt = Now.AddDays(1) };

        store.Exists.Should().BeFalse();
        store.SaveIfMissing(first).Should().BeTrue();
        store.SaveIfMissing(second).Should().BeFalse();

        store.Exists.Should().BeTrue();
        store.Load()!.CapturedAt.Should().Be(Now);
        store.Load()!.Registers.Should().Equal(first.Registers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ \"firmwareVersion\": \"16Q4EMS2.107\"")]
    [InlineData("{ \"firmwareVersion\": \"16Q4EMS2.107\", \"firmwareDate\": \"d\", \"capturedAt\": \"2026-09-30T12:00:00+00:00\", \"registers\": [] }")]
    public void A_truncated_or_incomplete_snapshot_does_not_count_as_saved(string content)
    {
        File.WriteAllText(Path.Combine(_folder, "pre-mtool-state.json"), content);
        var store = new PreStateStore(_folder);

        store.HasValidSnapshot("16Q4EMS2.107").Should().BeFalse();
        store.Load().Should().BeNull();
    }

    [Fact]
    public void A_snapshot_from_other_firmware_does_not_count()
    {
        var store = new PreStateStore(_folder);
        store.SaveIfMissing(PreStateCapture.Read(P65Memory.Faz0Snapshot(), new FirmwareInfo("16Q4EMS2.107", "d"), Now));

        store.HasValidSnapshot("16Q4EMS2.107").Should().BeTrue();
        store.HasValidSnapshot("16Q4EMS2.108").Should().BeFalse();
    }

    [Fact]
    public void Store_returns_null_when_nothing_was_saved()
    {
        new PreStateStore(_folder).Load().Should().BeNull();
    }

    /// <summary>Returns a different value for one register on every read.</summary>
    private sealed class ChangingRegisters(IEcRegisters inner, byte changing) : IEcRegisters
    {
        private byte _counter;

        public byte Read(byte register) => register == changing ? _counter++ : inner.Read(register);

        public IReadOnlyList<byte> ReadBlock(byte startRegister, int count) =>
            Enumerable.Range(startRegister, count).Select(r => Read((byte)r)).ToArray();
    }
}
