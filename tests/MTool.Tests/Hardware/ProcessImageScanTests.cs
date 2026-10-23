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
            () => [null, @"\\server\share\x.exe", @"\\?\C:\x.exe", @"C:\ok.exe"],
            path =>
            {
                reads.Add(path);
                return "Vendor";
            },
            Generous);

        reads.Should().Equal(@"C:\ok.exe");
        result.Companies.Should().Equal(null, null, null, "Vendor");
    }

    [Fact]
    public void A_failing_read_is_an_unknown_entry_and_the_scan_goes_on()
    {
        var result = ProcessImageScan.Run(
            () => [@"C:\a.exe", @"C:\b.exe", @"C:\c.exe"],
            path => path switch
            {
                @"C:\a.exe" => throw new IOException(),
                @"C:\b.exe" => throw new FormatException(),
                _ => "Vendor",
            },
            Generous);

        result.Companies.Should().Equal(null, null, "Vendor");
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
            var result = ProcessImageScan.Run(Slow, _ => "Micro-Star", TimeSpan.FromMilliseconds(300));

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
                TimeSpan.FromMilliseconds(300));

            result.Truncated.Should().BeTrue();
            result.Companies.Should().Equal("Vendor");
        }
        finally
        {
            release.Set();
        }
    }
}
