using MTool.App.ViewModels;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

public sealed class BandNotifierTests
{
    private readonly StatusViewModel _status = new(BandNotifier.Silent, new ImmediateDispatcher(), TimeProvider.System);

    [Fact]
    public void Good_news_goes_to_the_band_as_a_notice()
    {
        new BandNotifier(_status).ShowInfo("M-Tool", "Sent, thank you.");

        _status.Message.Should().Be("Sent, thank you.");
        _status.MessageKind.Should().Be(MessageKind.Info);
    }

    [Fact]
    public void A_problem_goes_to_the_band_as_a_warning_without_the_title()
    {
        new BandNotifier(_status).ShowError("M-Tool", "Could not reach the server.");

        _status.Message.Should().Be("Could not reach the server.");
        _status.MessageKind.Should().Be(MessageKind.Warning);
    }

    [Fact]
    public void The_silent_notifier_shows_nothing_anywhere()
    {
        BandNotifier.Silent.ShowError("M-Tool", "x");
        BandNotifier.Silent.ShowInfo("M-Tool", "x");

        _status.Message.Should().BeNull();
    }
}
