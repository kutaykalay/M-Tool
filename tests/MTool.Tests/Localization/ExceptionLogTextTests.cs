using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Localization;

/// <summary>An exception's message is in the user's language; what goes into the log is always English.</summary>
public sealed class ExceptionLogTextTests
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void Without_a_log_twin_the_log_text_is_the_message()
    {
        new InvalidOperationException("plain").ForLog().Should().Be("plain");
    }

    [Fact]
    public void A_log_twin_is_used_for_the_log_and_the_message_stays_the_users()
    {
        var error = new EcAccessException("Türkçe ileti").WithLog("English message");

        (error.Message, error.ForLog()).Should().Be(("Türkçe ileti", "English message"));
        error.ForUserAndLog().Should().Be(new UserAndLogText("Türkçe ileti", "English message"));
    }

    [Fact]
    public async Task A_paused_ec_is_told_to_the_user_in_turkish_and_logged_in_english()
    {
        using var _ = TestCulture.Use("tr");
        using var worker = new EcWorker(new FakeEcRegisters(), new FakeEcLock(), LockTimeout, accessGate: () => false);

        var error = await FluentActions.Awaiting(() => worker.RunAsync(ec => ec.Read(0x68)))
            .Should().ThrowAsync<EcAccessException>();

        error.Which.Message.Should().Be("EC erişimi uyku/uyanış nedeniyle duraklatıldı.");
        error.Which.ForLog().Should().Be("EC access is paused because of sleep or wake-up.");
    }

    [Fact]
    public async Task A_lock_timeout_is_logged_in_english()
    {
        using var _ = TestCulture.Use("tr");
        using var worker = new EcWorker(new FakeEcRegisters(), new FakeEcLock { Available = false }, LockTimeout);

        var error = await FluentActions.Awaiting(() => worker.RunAsync(ec => ec.Read(0x68)))
            .Should().ThrowAsync<EcAccessException>();

        error.Which.Message.Should().StartWith("EC kilidi (Access_EC) 500 ms");
        error.Which.ForLog().Should().StartWith("The EC lock (Access_EC) was not obtained within 500 ms");
    }

    [Fact]
    public void An_unknown_profile_is_logged_in_english()
    {
        using var _ = TestCulture.Use("tr");
        var desired = DesiredState.Default with { FanProfile = "Yok" };

        var plan = () => desired.ToPlans(ProfileCatalog.BuiltIn);

        var error = plan.Should().Throw<ArgumentException>().Which;
        error.ForLog().Should().NotBe(error.Message).And.Contain("Yok");
    }
}
