#if DEBUG
using MTool.App.Hardware;

namespace MTool.Tests.Hardware;

public class NoPawnIoPreviewTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("yes")]
    public void The_preview_is_off_unless_the_variable_is_1(string? value) =>
        NoPawnIoPreview.Parse(value).Should().BeFalse();

    [Theory]
    [InlineData("1")]
    [InlineData(" 1 ")]
    public void The_variable_turns_it_on(string value) =>
        NoPawnIoPreview.Parse(value).Should().BeTrue();

    [Fact]
    public void While_on_PawnIO_counts_as_missing_without_being_looked_up()
    {
        var looked = false;

        var installed = NoPawnIoPreview.Installed(previewOn: true, () => looked = true);

        installed().Should().BeFalse();
        looked.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void While_off_the_real_answer_is_kept(bool real) =>
        NoPawnIoPreview.Installed(previewOn: false, () => real)().Should().Be(real);
}
#endif
