using MTool.App.ViewModels;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

public class Wmi2LiveForTests
{
    private readonly ListLog _log = new();
    private int _opened;

    private Wmi2LiveViewModel? For(WmiInterface? detected) =>
        Wmi2LiveViewModel.For(detected, () =>
        {
            _opened++;
            return new FakeWmi2LiveSource();
        }, new FakeConfirm(), () => true, _log, list: null);

    [Theory]
    [InlineData(WmiInterface.Wmi1)]
    [InlineData(null)]
    public void There_is_no_live_section_unless_the_laptop_has_WMI2(WmiInterface? detected)
    {
        For(detected).Should().BeNull();
        _opened.Should().Be(0);
    }

    [Fact]
    public void On_a_WMI2_laptop_the_section_is_made_without_opening_or_reading_anything()
    {
        using var live = For(WmiInterface.Wmi2);

        live.Should().NotBeNull();
        live!.IsStarted.Should().BeFalse();
        live.ShowCommand.CanExecute(null).Should().BeTrue();
        _opened.Should().Be(0);
    }
}
