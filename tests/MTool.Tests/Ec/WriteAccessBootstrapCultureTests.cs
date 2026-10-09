using System.Text.RegularExpressions;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>
/// Why writes are locked at start, in Turkish, English and a language M-Tool lacks. Only the reason may
/// differ: the backup, the lock file, every EC access and the log must be the same. The Turkish
/// reasons are the ones from before the move to resources.
/// </summary>
public sealed partial class WriteAccessBootstrapCultureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 22, 0, 0, TimeSpan.FromHours(3));

    private static readonly Scenario[] Scenarios =
    [
        new("open", _ => { }),
        new("draft record", _ => { }, Draft: true),
        new("firmware not supported", folder => { }, Firmware: "16Q4EMS2.108"),
        new("backup is damaged", folder => File.WriteAllText(Path.Combine(folder, "pre-mtool-state.json"), "{ bozuk")),
        new("lock from an earlier session", folder => new WriteLockStore(folder).Lock("earlier session failed")),
        new("lock file cannot be read", _ => { }, HoldLockFile: true),
    ];

    private static readonly Dictionary<string, (string Turkish, string English)> Reasons = new()
    {
        ["draft record"] = (
            "değerler doğrulanmamış bir cihaz kaydıyla okunuyor; M-Tool'u yeniden başlatın.",
            "values are read with an unverified device record; restart M-Tool."),
        ["firmware not supported"] = (
            "bu firmware desteklenmiyor (yalnızca 16Q4EMS2.107).",
            "this firmware is not supported (only 16Q4EMS2.107)."),
        ["backup is damaged"] = (
            "ilk açılışta alınan ayar yedeği bulunamadı ya da bozuk.",
            "the settings backup taken at first launch is missing or damaged."),
    };

    public static TheoryData<string> Names() => [.. Scenarios.Select(s => s.Name)];

    /// <summary>The unreadable lock file puts the operating system's own error text in the log, in the system's language.</summary>
    public static TheoryData<string> NamesWithoutSystemText() => [.. Scenarios.Where(s => !s.HoldLockFile).Select(s => s.Name)];

    private static Scenario Find(string name) => Scenarios.Single(s => s.Name == name);

    private static async Task<(string? Reason, string Facts, IReadOnlyList<string> Log)> RunIn(string culture, Scenario scenario)
    {
        var folder = Directory.CreateTempSubdirectory("mtool-culture-").FullName;
        var ec = P65Memory.FactorySnapshot();
        var log = new ListLog();
        using var worker = new EcWorker(ec, new FakeEcLock(), TimeSpan.FromMilliseconds(50)); // thread started outside the culture under test
        try
        {
            if (scenario.Firmware is { } firmware)
            {
                ec.LoadAscii(0xA0, firmware);
            }

            scenario.Prepare(folder);
            using var held = scenario.HoldLockFile ? new FileStream(Path.Combine(folder, "write-lock.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None) : null;
            var layout = scenario.Draft
                ? DeviceLayout.From(DeviceConfigLoader.LoadEmbedded(new ListLog()).Single(c => c.Id == EmbeddedDevices.Wmi1GenericId)).WithoutPortFeatures()
                : TestLayouts.P65;

            using var _ = TestCulture.Use(culture);
            var setup = await WriteAccessBootstrap.CreateAsync(
                worker, layout, folder, dryRun: false, portAvailable: true, log, EcAccessRetry.Default with { Sleep = _ => { } }, () => Now);

            var reason = setup.Gateway.LockReason;
            var lines = log.Lines.Select(l => l.Replace(folder, "<folder>", StringComparison.Ordinal)).ToArray();
            var files = Directory.GetFiles(folder).Select(Path.GetFileName).Order();
            var facts = string.Join(
                "\n",
                $"firmware: {setup.Firmware?.Version}",
                $"write enabled: {setup.Gateway.IsWriteEnabled}",
                $"files: {string.Join(' ', files)}",
                $"writes: {string.Join(' ', ec.Writes.Select(w => $"{w.Register:X2}={w.Value:X2}"))}",
                $"accesses: {ec.Accesses}",
                $"log:\n{string.Join('\n', lines)}");
            return (reason, facts, lines);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Everything_but_the_reason_is_the_same_in_every_language(string name)
    {
        var scenario = Find(name);

        var turkish = await RunIn("tr", scenario);
        var english = await RunIn("en", scenario);
        var german = await RunIn("de", scenario);

        english.Facts.Should().Be(turkish.Facts);
        german.Facts.Should().Be(turkish.Facts);
        (english.Reason is null).Should().Be(turkish.Reason is null);
    }

    [Theory]
    [InlineData("draft record")]
    [InlineData("firmware not supported")]
    [InlineData("backup is damaged")]
    public async Task The_reason_is_Turkish_as_before_and_English_in_English(string name)
    {
        var turkish = await RunIn("tr", Find(name));
        var english = await RunIn("en", Find(name));
        var german = await RunIn("de", Find(name));

        turkish.Reason.Should().Be(Reasons[name].Turkish);
        english.Reason.Should().Be(Reasons[name].English).And.NotMatchRegex("[çğıöşüÇĞİÖŞÜ]");
        german.Reason.Should().Be(english.Reason);
    }

    [Fact]
    public async Task A_lock_file_that_cannot_be_read_locks_with_a_reason_in_the_user_language()
    {
        var turkish = await RunIn("tr", Find("lock file cannot be read"));
        var english = await RunIn("en", Find("lock file cannot be read"));

        turkish.Reason.Should().StartWith("kilit dosyası (write-lock.txt) okunamadı (").And.EndWith(").");
        english.Reason.Should().StartWith("the lock file (write-lock.txt) could not be read (").And.EndWith(").");
    }

    [Fact]
    public async Task A_lock_from_an_earlier_session_is_shown_as_it_was_saved_and_an_open_gateway_has_none()
    {
        var earlier = await RunIn("en", Find("lock from an earlier session"));
        var open = await RunIn("en", Find("open"));

        earlier.Reason.Should().EndWith("earlier session failed");
        open.Reason.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(NamesWithoutSystemText))]
    public async Task The_log_has_no_Turkish_text(string name)
    {
        var run = await RunIn("tr", Find(name));

        run.Log.Should().OnlyContain(line => !TurkishLetter().IsMatch(line));
    }

    [GeneratedRegex("[çğıöşüÇĞİÖŞÜ]")]
    private static partial Regex TurkishLetter();

    private sealed record Scenario(
        string Name,
        Action<string> Prepare,
        bool Draft = false,
        string? Firmware = null,
        bool HoldLockFile = false);
}
