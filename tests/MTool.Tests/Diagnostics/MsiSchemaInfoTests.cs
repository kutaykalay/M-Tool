using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

public class MsiSchemaInfoTests
{
    [Fact]
    public void No_signer_means_unsigned()
    {
        MsiSchemaInfo.ClassifySignature(null).Should().Be(SchemaSignature.Unsigned);
    }

    [Theory]
    [InlineData("CN=Micro-Star International Co., Ltd., O=Micro-Star International Co., Ltd., C=TW")]
    [InlineData("CN=MICRO-STAR INTERNATIONAL CO., LTD.")]
    public void A_micro_star_subject_is_micro_star(string subject)
    {
        MsiSchemaInfo.ClassifySignature(subject).Should().Be(SchemaSignature.MicroStar);
    }

    [Theory]
    [InlineData("CN=Someone Else, C=US")]
    [InlineData("")]
    public void Any_other_subject_is_other(string subject)
    {
        MsiSchemaInfo.ClassifySignature(subject).Should().Be(SchemaSignature.Other);
    }

    [Theory]
    [InlineData(MofImagePathState.NotSet, true)]
    [InlineData(MofImagePathState.Set, false)]
    [InlineData(MofImagePathState.Unreadable, false)]
    public void The_schema_is_missing_only_when_the_value_is_not_set(MofImagePathState state, bool expected)
    {
        new MsiSchemaInfo(state, SchemaFileState.Unknown, SchemaSignature.Unknown).SchemaMissing.Should().Be(expected);
    }

    [Theory]
    [InlineData("Micro-Star INT'L CO., LTD.", true)]
    [InlineData("micro-star international", true)]
    [InlineData("Microsoft Corporation", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Vendor_text_is_micro_star_only_when_it_says_so(string? text, bool expected)
    {
        MsiSoftwareInfo.IsMicroStar(text).Should().Be(expected);
    }

    [Fact]
    public void One_hit_decides_even_when_other_answers_are_unknown()
    {
        MsiSoftwareInfo.Decide([null, false, true]).Should().BeTrue();
    }

    [Fact]
    public void Misses_without_a_hit_decide_no()
    {
        MsiSoftwareInfo.Decide([false, null, false]).Should().BeFalse();
    }

    [Fact]
    public void Nothing_read_at_all_decides_unknown()
    {
        MsiSoftwareInfo.Decide([null, null]).Should().BeNull();
        MsiSoftwareInfo.Decide([]).Should().BeNull();
    }
}
