using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>
/// The port protocol's "all attempts failed" message reaches the user inside the gateway's "could not reach the
/// laptop" text, so it follows the UI language; the log always gets the English twin.
/// </summary>
public sealed class EcControllerCultureTests
{
    private static readonly EcProtocolOptions FastOptions = new(MaxAttempts: 2, MaxStatusPolls: 20);

    private static EcAccessException FailedReadIn(string culture)
    {
        var ec = new SimulatedEc { Hung = true };
        var controller = new EcController(ec, FastOptions);
        using var _ = TestCulture.Use(culture);

        return Assert.Throws<EcAccessException>(() => controller.Read(0xEF));
    }

    private static EcAccessException FailedWriteIn(string culture)
    {
        var ec = new SimulatedEc { Hung = true };
        IEcWritableRegisters controller = new EcController(ec, FastOptions);
        using var _ = TestCulture.Use(culture);

        return Assert.Throws<EcAccessException>(() => controller.Write(0xF2, 0xC1));
    }

    [Fact]
    public void A_failed_read_is_in_the_language_of_the_user_and_English_in_the_log()
    {
        var turkish = FailedReadIn("tr");
        var english = FailedReadIn("en");
        var finnish = FailedReadIn("fi");

        turkish.Message.Should().StartWith("EC register 0xEF 2 denemede okunamadı (");
        english.Message.Should().StartWith("EC register 0xEF could not be read in 2 attempts (");
        finnish.Message.Should().Be(english.Message);
        turkish.ForLog().Should().Be(english.Message);
    }

    [Fact]
    public void A_failed_write_is_in_the_language_of_the_user_and_English_in_the_log()
    {
        var turkish = FailedWriteIn("tr");
        var english = FailedWriteIn("en");

        turkish.Message.Should().StartWith("EC register 0xF2 2 denemede yazılamadı (");
        english.Message.Should().StartWith("EC register 0xF2 could not be written in 2 attempts (");
        turkish.ForLog().Should().Be(english.Message);
    }
}
