using MTool.App.Hardware;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Hardware;

/// <summary>The decisions of the probe against fake system reads; the real registry and processes are checked by hand.</summary>
public class MsiSoftwareProbeTests
{
    private const string RawPath = @"%windir%\sysWOW64\msiapcfg.dll";
    private const string ExpandedPath = @"C:\Windows\sysWOW64\msiapcfg.dll";

    internal static MsiSoftwareSources Sources(
        Func<string?>? mofImagePath = null,
        Func<string, bool>? fileExists = null,
        Func<string, string?>? signer = null,
        Func<IReadOnlyList<IReadOnlyList<string?>?>>? publishers = null,
        Func<IReadOnlyList<string?>?>? companies = null,
        Func<ProcessScanResult?>? scan = null,
        Func<string, bool>? isFixedDisk = null) => new(
            mofImagePath ?? (() => RawPath),
            () => @"C:\Windows",
            fileExists ?? (_ => true),
            signer ?? (_ => null),
            publishers ?? (() => [[], []]),
            scan ?? (() => companies?.Invoke() is { } list ? new ProcessScanResult(list, false) : null),
            isFixedDisk ?? (_ => true));

    [Fact]
    public void A_missing_value_means_the_schema_is_not_installed()
    {
        var info = new MsiSoftwareProbe(Sources(mofImagePath: () => null)).ReadSchema();

        info.Should().Be(new MsiSchemaInfo(MofImagePathState.NotSet, SchemaFileState.Unknown, SchemaSignature.Unknown));
        info.SchemaMissing.Should().BeTrue();
    }

    [Fact]
    public void An_unreadable_registry_is_not_a_missing_schema()
    {
        var info = new MsiSoftwareProbe(Sources(mofImagePath: () => throw new UnauthorizedAccessException())).ReadSchema();

        info.Should().Be(new MsiSchemaInfo(MofImagePathState.Unreadable, SchemaFileState.Unknown, SchemaSignature.Unknown));
        info.SchemaMissing.Should().BeFalse();
    }

    [Fact]
    public void An_unsigned_file_that_exists_is_reported_as_such()
    {
        string? checkedPath = null;
        var sources = Sources(fileExists: path =>
        {
            checkedPath = path;
            return true;
        });

        new MsiSoftwareProbe(sources).ReadSchema()
            .Should().Be(new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Exists, SchemaSignature.Unsigned));
        checkedPath.Should().Be(ExpandedPath);
    }

    [Fact]
    public void A_missing_file_is_reported_without_reading_a_signature()
    {
        var sources = Sources(fileExists: _ => false, signer: _ => throw new InvalidOperationException("must not be read"));

        new MsiSoftwareProbe(sources).ReadSchema()
            .Should().Be(new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Missing, SchemaSignature.Unknown));
    }

    [Theory]
    [InlineData("CN=Micro-Star International Co., Ltd.", SchemaSignature.MicroStar)]
    [InlineData("CN=Other Vendor", SchemaSignature.Other)]
    public void A_signed_file_is_classified_by_its_signer(string subject, SchemaSignature expected)
    {
        new MsiSoftwareProbe(Sources(signer: _ => subject)).ReadSchema().Signature.Should().Be(expected);
    }

    [Fact]
    public void A_signature_that_cannot_be_read_is_unknown()
    {
        new MsiSoftwareProbe(Sources(signer: _ => throw new IOException())).ReadSchema().Signature.Should().Be(SchemaSignature.Unknown);
    }

    [Fact]
    public void A_failing_file_check_is_unknown_not_an_exception()
    {
        var info = new MsiSoftwareProbe(Sources(fileExists: _ => throw new UnauthorizedAccessException())).ReadSchema();

        info.Should().Be(new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Unknown, SchemaSignature.Unknown));
    }

    [Fact]
    public void A_micro_star_uninstall_entry_in_either_hive_means_installed()
    {
        var sources = Sources(publishers: () => [["Microsoft"], ["Micro-Star INT'L CO., LTD."]]);

        new MsiSoftwareProbe(sources).ReadSoftware().Installed.Should().BeTrue();
    }

    [Fact]
    public void No_micro_star_entry_means_not_installed()
    {
        var sources = Sources(publishers: () => [["Microsoft", null], []]);

        new MsiSoftwareProbe(sources).ReadSoftware().Installed.Should().BeFalse();
    }

    [Fact]
    public void Hives_that_cannot_be_read_leave_installed_unknown()
    {
        new MsiSoftwareProbe(Sources(publishers: () => [null, null])).ReadSoftware().Installed.Should().BeNull();
        new MsiSoftwareProbe(Sources(publishers: () => throw new InvalidOperationException())).ReadSoftware().Installed.Should().BeNull();
    }

    [Fact]
    public void One_readable_hive_is_enough_for_a_no()
    {
        new MsiSoftwareProbe(Sources(publishers: () => [null, ["Microsoft"]])).ReadSoftware().Installed.Should().BeFalse();
    }

    [Fact]
    public void A_running_micro_star_program_is_found_among_unreadable_ones()
    {
        var sources = Sources(companies: () => [null, "Microsoft Corporation", "Micro-Star International"]);

        new MsiSoftwareProbe(sources).ReadSoftware().Running.Should().BeTrue();
    }

    [Fact]
    public void Readable_programs_without_a_micro_star_one_mean_not_running()
    {
        new MsiSoftwareProbe(Sources(companies: () => [null, "Microsoft Corporation", ""])).ReadSoftware().Running.Should().BeFalse();
    }

    [Fact]
    public void When_no_program_could_be_read_running_is_unknown()
    {
        new MsiSoftwareProbe(Sources(companies: () => [null, null])).ReadSoftware().Running.Should().BeNull();
        new MsiSoftwareProbe(Sources(companies: () => null)).ReadSoftware().Running.Should().BeNull();
        new MsiSoftwareProbe(Sources(companies: () => throw new InvalidOperationException())).ReadSoftware().Running.Should().BeNull();
    }
}
