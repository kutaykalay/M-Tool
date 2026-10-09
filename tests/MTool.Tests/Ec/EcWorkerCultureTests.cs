using System.Globalization;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>
/// The two exception messages of <see cref="EcWorker"/> reach the user inside the gateway's "could not reach the
/// laptop" text. They follow the language of the code that asked for the operation, not the language of the
/// worker's own thread, and the operation itself behaves the same in every language.
/// </summary>
public sealed class EcWorkerCultureTests
{
    private static async Task<(EcAccessException Error, int Accesses, bool LockTaken)> LockBusyIn(string culture)
    {
        var ec = P65Memory.FactorySnapshot();
        var ecLock = new FakeEcLock { Available = false };
        using var worker = new EcWorker(ec, ecLock, TimeSpan.FromMilliseconds(50)); // thread started outside the culture under test
        using var _ = TestCulture.Use(culture);

        var error = await Assert.ThrowsAsync<EcAccessException>(() => worker.RunAsync(registers => registers.Read(0xF2)));

        return (error, ec.Accesses, ecLock.Held);
    }

    private static async Task<(EcAccessException Error, int Accesses)> PausedIn(string culture)
    {
        var ec = P65Memory.FactorySnapshot();
        using var worker = new EcWorker(ec, new FakeEcLock(), TimeSpan.FromMilliseconds(50), accessGate: () => false);
        using var _ = TestCulture.Use(culture);

        var error = await Assert.ThrowsAsync<EcAccessException>(() => worker.RunAsync(registers => registers.Read(0xF2)));

        return (error, ec.Accesses);
    }

    [Fact]
    public async Task A_busy_lock_is_reported_in_the_language_of_the_caller()
    {
        var turkish = await LockBusyIn("tr");
        var english = await LockBusyIn("en");
        var german = await LockBusyIn("de");

        turkish.Error.Message.Should().Be("EC kilidi (Access_EC) 50 ms içinde alınamadı; başka bir program EC'yi kullanıyor olabilir.");
        english.Error.Message.Should().Be("The EC lock (Access_EC) was not obtained within 50 ms; another program may be using the EC.");
        german.Error.Message.Should().Be(english.Error.Message);
        new[] { turkish, english, german }.Should().OnlyContain(r => r.Accesses == 0 && !r.LockTaken && !r.Error.IsAccessPaused);
    }

    [Fact]
    public async Task A_paused_EC_is_reported_in_the_language_of_the_caller()
    {
        var turkish = await PausedIn("tr");
        var english = await PausedIn("en");
        var german = await PausedIn("de");

        turkish.Error.Message.Should().Be("EC erişimi uyku/uyanış nedeniyle duraklatıldı.");
        english.Error.Message.Should().Be("EC access is paused because of sleep or wake-up.");
        german.Error.Message.Should().Be(english.Error.Message);
        new[] { turkish, english, german }.Should().OnlyContain(r => r.Accesses == 0 && r.Error.IsAccessPaused);
    }

    [Fact]
    public async Task The_operation_itself_runs_the_same_in_every_language()
    {
        var ec = P65Memory.FactorySnapshot();
        using var worker = new EcWorker(ec, new FakeEcLock(), TimeSpan.FromMilliseconds(50));
        var results = new List<byte>();

        foreach (var culture in new[] { "tr", "en", "de" })
        {
            using var _ = TestCulture.Use(culture);
            results.Add(await worker.RunAsync(registers => registers.Read(0xF2)));
        }

        results.Should().OnlyContain(value => value == ec[0xF2]);
    }

    [Fact]
    public async Task The_caller_culture_does_not_stay_on_the_worker_thread()
    {
        var ec = P65Memory.FactorySnapshot();
        using var worker = new EcWorker(ec, new FakeEcLock(), TimeSpan.FromMilliseconds(50));
        var before = await worker.RunAsync(_ => CultureInfo.CurrentUICulture.Name);
        using (TestCulture.Use("en"))
        {
            await worker.RunAsync(_ => 0);
        }

        var after = await worker.RunAsync(_ => CultureInfo.CurrentUICulture.Name);

        after.Should().Be(before);
    }
}
