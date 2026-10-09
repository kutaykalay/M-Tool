using MTool.App.Hardware;
using MTool.Core.Ec;

namespace MTool.Tests.Hardware;

public sealed class PawnIoMissingExceptionTests
{
    [Fact]
    public void The_message_is_for_the_window_and_the_log_text_is_English_with_the_same_install_command()
    {
        var exception = new PawnIoMissingException();

        exception.Message.Should().Contain("kurulu değil").And.Contain(PawnIoInstallation.InstallCommand);
        exception.LogMessage.Should().Contain("PawnIO is not installed").And.Contain(PawnIoInstallation.InstallCommand);
    }
}
