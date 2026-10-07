using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

public class FieldScanTests
{
    private static readonly IReadOnlyList<(byte Register, string Field)> Fields =
        [(0x68, "MSI_CPU[1]"), (0x6A, "MSI_CPU[5]"), (0x6B, "MSI_CPU[6]"), (0x6C, "MSI_CPU[7]"), (0x6D, "MSI_CPU[8]"), (0x71, "MSI_CPU[2]")];

    [Fact]
    public void Reads_every_field_in_order()
    {
        var scan = FieldScan.Run(Fields, register => register);

        scan.Readouts.Select(r => (r.Register, r.Value)).Should().Equal(
            (0x68, (byte?)0x68), (0x6A, 0x6A), (0x6B, 0x6B), (0x6C, 0x6C), (0x6D, 0x6D), (0x71, 0x71));
        scan.Skipped.Should().Be(0);
    }

    [Fact]
    public void A_failed_field_is_kept_as_failed_and_the_scan_goes_on()
    {
        var scan = FieldScan.Run(Fields, register => register == 0x6A ? null : register);

        scan.Readouts.Should().HaveCount(6);
        scan.Readouts[1].Should().Be(new FieldReadout(0x6A, "MSI_CPU[5]", null));
        scan.Skipped.Should().Be(0);
    }

    [Fact]
    public void Stops_after_three_failures_in_a_row_and_keeps_what_it_read()
    {
        var reads = new List<byte>();

        var scan = FieldScan.Run(Fields, register =>
        {
            reads.Add(register);
            return register == 0x68 ? register : null;
        });

        reads.Should().Equal(0x68, 0x6A, 0x6B, 0x6C);
        scan.Readouts.Select(r => r.Value).Should().Equal(0x68, null, null, null);
        scan.Skipped.Should().Be(2);
    }

    [Fact]
    public void A_success_resets_the_failure_count()
    {
        var scan = FieldScan.Run(Fields, register => register is 0x6A or 0x6B or 0x6D or 0x71 ? null : register);

        scan.Readouts.Should().HaveCount(6);
        scan.Skipped.Should().Be(0);
    }
}
