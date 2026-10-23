using MTool.App.Hardware;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Hardware;

/// <summary>A hostile environment, odd files, slow or failing reads: the probe answers "unknown", never throws and never trusts a path it cannot vouch for.</summary>
public class MsiSoftwareProbeHardeningTests
{
    [Theory]
    [InlineData(@"%ProgramData%\msiapcfg.dll")]
    [InlineData(@"%TEMP%\msiapcfg.dll")]
    [InlineData(@"\\server\share\msiapcfg.dll")]
    [InlineData(@"\\?\C:\Windows\msiapcfg.dll")]
    [InlineData(@"relative\msiapcfg.dll")]
    public void A_path_that_cannot_be_trusted_touches_no_file(string raw)
    {
        var sources = MsiSoftwareProbeTests.Sources(
            mofImagePath: () => raw,
            fileExists: _ => throw new InvalidOperationException("must not be checked"),
            signer: _ => throw new InvalidOperationException("must not be read"));

        new MsiSoftwareProbe(sources).ReadSchema()
            .Should().Be(new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Unknown, SchemaSignature.Unknown));
    }

    [Fact]
    public void Expansion_uses_the_given_windows_directory_not_the_environment()
    {
        string? checkedPath = null;
        var sources = MsiSoftwareProbeTests.Sources(mofImagePath: () => @"%windir%\x.dll", fileExists: path =>
        {
            checkedPath = path;
            return false;
        });

        new MsiSoftwareProbe(sources).ReadSchema();

        checkedPath.Should().Be(@"C:\Windows\x.dll");
    }

    [Fact]
    public void An_unexpected_exception_gives_unknown_answers_instead_of_surfacing()
    {
        var unreadable = new MsiSchemaInfo(MofImagePathState.Unreadable, SchemaFileState.Unknown, SchemaSignature.Unknown);

        new MsiSoftwareProbe(MsiSoftwareProbeTests.Sources(mofImagePath: () => throw new FormatException())).ReadSchema()
            .Should().Be(unreadable);
        new MsiSoftwareProbe(MsiSoftwareProbeTests.Sources(fileExists: _ => throw new FormatException())).ReadSchema()
            .Should().Be(unreadable);
        new MsiSoftwareProbe(MsiSoftwareProbeTests.Sources(signer: _ => throw new FormatException())).ReadSchema()
            .Should().Be(unreadable);
        new MsiSoftwareProbe(MsiSoftwareProbeTests.Sources(
            publishers: () => throw new FormatException(),
            scan: () => throw new NullReferenceException())).ReadSoftware()
            .Should().Be(new MsiSoftwareInfo(null, null));
    }

    [Fact]
    public void Fatal_exceptions_propagate()
    {
        var schema = new MsiSoftwareProbe(MsiSoftwareProbeTests.Sources(mofImagePath: () => throw new OutOfMemoryException()));
        var software = new MsiSoftwareProbe(MsiSoftwareProbeTests.Sources(scan: () => throw new OutOfMemoryException()));

        schema.Invoking(probe => probe.ReadSchema()).Should().Throw<OutOfMemoryException>();
        software.Invoking(probe => probe.ReadSoftware()).Should().Throw<OutOfMemoryException>();
    }

    [Fact]
    public void A_cut_off_scan_without_a_hit_is_unknown_not_no()
    {
        var sources = MsiSoftwareProbeTests.Sources(scan: () => new ProcessScanResult(["Microsoft Corporation", null], Truncated: true));

        new MsiSoftwareProbe(sources).ReadSoftware().Running.Should().BeNull();
    }

    [Fact]
    public void A_hit_found_before_the_cut_off_still_wins()
    {
        var sources = MsiSoftwareProbeTests.Sources(scan: () => new ProcessScanResult(["Micro-Star International"], Truncated: true));

        new MsiSoftwareProbe(sources).ReadSoftware().Running.Should().BeTrue();
    }

    [Fact]
    public void A_bad_uninstall_entry_does_not_hide_the_rest_of_the_hive()
    {
        var publishers = MsiSoftwareProbe.ReadEntries(["a", "bad", "c"], name => name switch
        {
            "bad" => throw new IOException(),
            "c" => "Micro-Star INT'L CO., LTD.",
            _ => "Microsoft",
        });

        publishers.Should().Equal("Microsoft", null, "Micro-Star INT'L CO., LTD.");
    }

    [Fact]
    public void A_path_on_a_disk_that_is_not_fixed_touches_no_file()
    {
        var sources = MsiSoftwareProbeTests.Sources(
            isFixedDisk: _ => false,
            fileExists: _ => throw new InvalidOperationException("must not be checked"),
            signer: _ => throw new InvalidOperationException("must not be read"));

        new MsiSoftwareProbe(sources).ReadSchema()
            .Should().Be(new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Unknown, SchemaSignature.Unknown));
    }

    [Fact]
    public void A_failing_disk_type_check_is_unknown_not_an_exception()
    {
        var sources = MsiSoftwareProbeTests.Sources(isFixedDisk: _ => throw new IOException());

        new MsiSoftwareProbe(sources).ReadSchema()
            .Should().Be(new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Unknown, SchemaSignature.Unknown));
    }

    [Fact]
    public void A_hive_where_every_entry_failed_is_unknown_not_a_miss()
    {
        MsiSoftwareProbe.ReadEntries(["a", "b"], _ => throw new IOException()).Should().BeNull();
        MsiSoftwareProbe.ReadEntries([], _ => throw new IOException()).Should().BeEmpty();
    }

    [Fact]
    public void A_fatal_exception_in_an_entry_propagates()
    {
        var act = () => MsiSoftwareProbe.ReadEntries(["a"], _ => throw new OutOfMemoryException());

        act.Should().Throw<OutOfMemoryException>();
    }
    [Fact]
    public void An_empty_file_reads_as_a_signature_that_cannot_be_read()
    {
        ProbeSignatureOfTempFile([]).Should().Be(SchemaSignature.Unknown);
    }

    [Fact]
    public void A_text_file_reads_as_unsigned()
    {
        ProbeSignatureOfTempFile("not a PE file"u8.ToArray()).Should().Be(SchemaSignature.Unsigned);
    }

    private static SchemaSignature ProbeSignatureOfTempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mtool-probe-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, content);
        try
        {
            var sources = MsiSoftwareProbeTests.Sources(mofImagePath: () => path, fileExists: File.Exists, signer: AuthenticodeSigner.ReadSubject);
            return new MsiSoftwareProbe(sources).ReadSchema().Signature;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
