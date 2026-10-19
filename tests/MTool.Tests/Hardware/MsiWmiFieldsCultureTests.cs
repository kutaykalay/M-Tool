using MTool.App.Hardware;
using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Tests.Fakes;

namespace MTool.Tests.Hardware;

/// <summary>
/// A WMI failure reaches the user in the "could not reach the laptop" texts, so it follows the UI language; the
/// log always gets the English twin. The name of the call stays a technical English label.
/// </summary>
public sealed class MsiWmiFieldsCultureTests
{
    private static EcAccessException FailureIn(string culture)
    {
        using var _ = TestCulture.Use(culture);
        return Assert.Throws<EcAccessException>(
            () => MsiWmiFields.Bounded<int>("MSI_CPU read", TimeSpan.FromSeconds(1), () => throw new TimeoutException("system text")));
    }

    private static EcAccessException HangIn(string culture)
    {
        using var never = new ManualResetEventSlim();
        using var _ = TestCulture.Use(culture);
        try
        {
            return Assert.Throws<EcAccessException>(
                () => MsiWmiFields.Bounded("connect", TimeSpan.FromMilliseconds(100), () => never.Wait(TimeSpan.FromSeconds(5))));
        }
        finally
        {
            never.Set();
        }
    }

    private static EcAccessException OddInstancesIn(string culture)
    {
        using var _ = TestCulture.Use(culture);
        return Assert.Throws<EcAccessException>(() => MsiWmiFields.PrefixOf(["no-underscore"]));
    }

    [Fact]
    public void A_failed_call_is_in_the_language_of_the_user_and_English_in_the_log()
    {
        var turkish = FailureIn("tr");
        var english = FailureIn("en");

        turkish.Message.Should().Be("WMI çağrısı (MSI_CPU read) başarısız: system text");
        english.Message.Should().Be("WMI call (MSI_CPU read) failed: system text");
        turkish.ForLog().Should().Be(english.Message);
    }

    [Fact]
    public void A_call_without_an_answer_is_in_the_language_of_the_user_and_English_in_the_log()
    {
        var turkish = HangIn("tr");
        var english = HangIn("en");

        turkish.Message.Should().Be("WMI çağrısı (connect) 0,1 sn içinde cevap vermedi (zaman aşımı).");
        english.Message.Should().Be("WMI call (connect) gave no answer within 0.1 s (timeout).");
        turkish.ForLog().Should().Be(english.Message);
    }

    [Fact]
    public void An_unexpected_answer_is_in_the_language_of_the_user_with_the_detail_in_English()
    {
        var turkish = OddInstancesIn("tr");
        var english = OddInstancesIn("en");

        turkish.Message.Should().Be("WMI beklenmeyen bir cevap verdi: MSI_Software instance names: no-underscore");
        english.Message.Should().Be("WMI gave an unexpected answer: MSI_Software instance names: no-underscore");
        turkish.ForLog().Should().Be(english.Message);
    }
}
