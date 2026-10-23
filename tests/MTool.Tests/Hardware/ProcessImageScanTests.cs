using MTool.App.Hardware;

namespace MTool.Tests.Hardware;

/// <summary>The scan against fake image lists; no real process is looked at.</summary>
public class ProcessImageScanTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

    [Fact]
    public void Each_distinct_image_is_read_once()
    {
        var reads = new List<string>();

        var result = ProcessImageScan.Run(
            () => [@"C:\a\x.exe", @"C:\A\X.EXE", @"C:\b\y.exe", @"C:\a\x.exe"],
            path =>
            {
                reads.Add(path);
                return "Vendor";
            },
            _ => true,
            Generous);

        reads.Should().Equal(@"C:\a\x.exe", @"C:\b\y.exe");
        result.Companies.Should().Equal("Vendor", "Vendor");
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void Network_and_unqueryable_images_are_skipped_as_unknown()
    {
        var reads = new List<string>();

        var result = ProcessImageScan.Run(
            () => [null, @"\\server\share\x.exe", @"\\?\C:\x.exe", @"C:\ok.exe", @"C:\ok2.exe"],
            path =>
            {
                reads.Add(path);
                return "Vendor";
            },
            _ => true,
            Generous);

        reads.Should().Equal(@"C:\ok.exe", @"C:\ok2.exe");
        // A null image path (not queryable) is not counted; the two skipped paths are.
        result.Companies.Should().Equal(null, null, "Vendor", "Vendor");
    }

    [Fact]
    public void A_failing_read_is_an_unknown_entry_and_the_scan_goes_on()
    {
        var result = ProcessImageScan.Run(
            () => [@"C:\a.exe", @"C:\b.exe", @"C:\c.exe", @"C:\d.exe"],
            path => path switch
            {
                @"C:\a.exe" => throw new IOException(),
                @"C:\b.exe" => throw new FormatException(),
                _ => "Vendor",
            },
            _ => true,
            Generous);

        result.Companies.Should().Equal(null, null, "Vendor", "Vendor");
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void A_scan_that_outlasts_the_budget_is_cut_off_and_keeps_what_it_found()
    {
        using var release = new ManualResetEventSlim();
        IEnumerable<string?> Slow()
        {
            yield return @"C:\fast.exe";
            release.Wait(TimeSpan.FromSeconds(30));
            yield return @"C:\late.exe";
        }

        try
        {
            var result = ProcessImageScan.Run(Slow, _ => "Micro-Star", _ => true, TimeSpan.FromMilliseconds(300));

            result.Truncated.Should().BeTrue();
            result.Companies.Should().Equal("Micro-Star");
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void A_slow_read_also_hits_the_budget()
    {
        using var release = new ManualResetEventSlim();
        try
        {
            var result = ProcessImageScan.Run(
                () => [@"C:\a.exe", @"C:\b.exe"],
                path =>
                {
                    if (path == @"C:\b.exe")
                    {
                        release.Wait(TimeSpan.FromSeconds(30));
                    }

                    return "Vendor";
                },
                _ => true,
                TimeSpan.FromMilliseconds(300));

            result.Truncated.Should().BeTrue();
            result.Companies.Should().Equal("Vendor");
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void An_image_on_a_disk_that_is_not_fixed_is_skipped_unread()
    {
        var reads = new List<string>();

        var result = ProcessImageScan.Run(
            () => [@"Z:\net.exe", @"C:\local.exe", @"D:\other.exe"],
            path =>
            {
                reads.Add(path);
                return "Vendor";
            },
            path => path[0] == 'C',
            Generous);

        reads.Should().Equal(@"C:\local.exe");
        result.Companies.Should().Equal(null, "Vendor", null);
    }

    [Fact]
    public void A_failing_disk_type_check_is_an_unknown_entry()
    {
        var result = ProcessImageScan.Run(
            () => [@"C:\a.exe"],
            _ => throw new InvalidOperationException("must not be read"),
            _ => throw new IOException(),
            Generous);

        result.Companies.Should().Equal((string?)null);
    }

    [Fact]
    public void Too_few_readable_programs_make_the_scan_count_as_cut_off()
    {
        var result = ProcessImageScan.Run(
            () => [@"C:\a.exe", @"C:\b.exe", @"C:\c.exe", @"C:\d.exe"],
            path => path == @"C:\d.exe" ? "Micro-Star" : throw new IOException(),
            _ => true,
            Generous);

        result.Truncated.Should().BeTrue();
        result.Companies.Should().Equal(null, null, null, "Micro-Star");
    }

    [Fact]
    public void Exactly_half_readable_is_enough()
    {
        var result = ProcessImageScan.Run(
            () => [@"C:\a.exe", @"C:\b.exe"],
            path => path == @"C:\a.exe" ? "Vendor" : throw new IOException(),
            _ => true,
            Generous);

        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void Programs_without_an_image_path_do_not_count_against_coverage()
    {
        var result = ProcessImageScan.Run(
            () => [null, null, null, null, @"C:\a.exe"],
            _ => "Vendor",
            _ => true,
            Generous);

        result.Truncated.Should().BeFalse();
        result.Companies.Should().Equal("Vendor");
    }
}
