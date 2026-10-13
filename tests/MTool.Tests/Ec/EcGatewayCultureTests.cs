using System.Text.RegularExpressions;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>
/// The write path in Turkish, English and a language M-Tool lacks (Finnish, which falls back to English).
/// Only the text for the user may differ: status, planned writes, every EC access, the lock file and the
/// log must be identical. The Turkish texts are the ones from before the move to resources.
/// </summary>
public sealed partial class EcGatewayCultureTests
{
    private static readonly WritePolicy Live = new(FirmwareSupported: true, PreStateSaved: true, DryRun: false, PortAvailable: true);

    private static readonly Scenario[] Scenarios =
    [
        new("firmware not supported", r => One(r.Gateway(Live with { FirmwareSupported = false }), WritePlans.ChargeLimit(79))),
        new("layout not verified", r => One(r.Gateway(Live with { LayoutVerified = false }), WritePlans.ChargeLimit(79))),
        new("no settings backup", r => One(r.Gateway(Live with { PreStateSaved = false }), WritePlans.ChargeLimit(79))),
        new("lock from an earlier session", r => One(r.Gateway(Live with { PersistedLockReason = "earlier session failed" }), WritePlans.ChargeLimit(79))),
        new("empty plan", r => One(r.Gateway(), new WritePlan("test", []))),
        new("duplicate register", r => One(r.Gateway(), new WritePlan("test", [new(0xF2, 0xC1), new(0xF2, 0xC2)]))),
        new("port plan without a port", r => One(r.Gateway(Live with { PortAvailable = false }), WritePlans.ChargeLimit(79))),
        new("port register not alone", r => One(r.Gateway(), new WritePlan("test", [new(0xEF, 0x80 | 79), new(0x72, 50)]))),
        new("register not on the whitelist", r => One(r.Gateway(), new WritePlan("test", [new(0xF3, 0x83)]))),
        new("cooler boost touches other bits", r => One(r.Gateway(), new WritePlan("test", [new(0x98, 0x80)]))),
        new("unsafe fan table", r => One(r.Gateway(), new WritePlan("test", [new(0x78, 70)]))),
        new("fan mode on an unsafe table", r =>
        {
            r.Ec[0x78] = 50;
            return One(r.Gateway(), WritePlans.Fan(FanMode.Advanced));
        }),
        new("dry run", r => One(r.Gateway(Live with { DryRun = true }), WritePlans.FanCurves(Presets.Cool.Curves, "Cool"))),
        new("applied", r => One(r.Gateway(), WritePlans.FanCurves(Presets.Cool.Curves, "Cool"))),
        new("applied after a retry", r =>
        {
            r.Ec.FlakyOnceRegisters.Add(0xEF);
            return One(r.Gateway(), WritePlans.ChargeLimit(79));
        }),
        new("fan write read back differently", r =>
        {
            r.Ec.StuckRegisters.Add(0x72);
            r.Ec[0x72] = 45;
            return One(r.Gateway(), WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));
        }),
        new("factory table cannot be restored", r =>
        {
            r.Ec.StuckRegisters.Add(0x72);
            r.Ec[0x72] = 99;
            return One(r.Gateway(), WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));
        }),
        new("factory table cannot be restored, no port", r =>
        {
            r.Ec.StuckRegisters.Add(0x72);
            r.Ec[0x72] = 99;
            return One(r.Gateway(Live with { PortAvailable = false }), WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));
        }),
        new("cooler boost cannot be restored", r =>
        {
            r.Ec.StuckRegisters.Add(0x72);
            r.Ec[0x72] = 99;
            byte flip = 0;
            r.ReadHook = register => register == 0x98 ? (byte)(flip++ % 2 == 0 ? 0x02 : 0x42) : null;
            return One(r.Gateway(), WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));
        }),
        new("EC access error while writing", r =>
        {
            r.Ec.AccessError = new EcAccessException("EC hung");
            return One(r.Gateway(), WritePlans.Performance(PerformanceMode.Balanced));
        }),
        new("second plan after a failure", async r =>
        {
            r.Ec.StuckRegisters.Add(0xF2);
            var gateway = r.Gateway();
            var first = gateway.ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced));
            var second = gateway.ApplyAsync(WritePlans.ChargeLimit(79));
            return new[] { await first, await second };
        }),
        new("EC lock busy", r =>
        {
            r.EcLock.Available = false;
            return One(r.Gateway(), WritePlans.Performance(PerformanceMode.Balanced));
        }, ExceptionInMessage: true),
        new("port read fails before writing", r =>
        {
            r.ReadHook = register => register == 0x6A ? throw new EcAccessException("EC silent") : null;
            return One(r.Gateway(), WritePlans.ChargeLimit(79));
        }),
        new("port write disturbs performance mode", r =>
        {
            r.Ec.AfterWrite = written => r.Ec[0xF2] = written == EcMap.ChargeLimit ? (byte)0xC4 : r.Ec[0xF2];
            return One(r.Gateway(), WritePlans.ChargeLimit(79));
        }),
        new("port write disturbs the fan table", r =>
        {
            r.Ec.AfterWrite = written => r.Ec[0x6A] = written == EcMap.ChargeLimit ? (byte)50 : r.Ec[0x6A];
            return One(r.Gateway(), WritePlans.ChargeLimit(79));
        }),
    ];

    private const string TurkishReadBack = "Ayar uygulanamadı. Ayar yazıldı ama geri okunduğunda farklı çıktı (0x72=0x32).";
    private const string EnglishReadBack = "The setting could not be applied. The setting was written but read back differently (0x72=0x32).";
    private const string TurkishUnreachable = "Dizüstüne ulaşılamadı, hiçbir ayar değişmedi. Biraz sonra tekrar deneyin. (";
    private const string EnglishUnreachable = "Could not reach the laptop; no setting was changed. Try again in a moment. (";
    private const string TurkishSecondRead = "Ayar yazıldı ama geri okunduğunda farklı çıktı (0xF2=0xC1)";
    private const string EnglishSecondRead = "The setting was written but read back differently (0xF2=0xC1)";

    /// <summary>What the user reads per scenario. The Turkish column is copied from the code before the move to resources.</summary>
    private static readonly Dictionary<string, (string Turkish, string English)> Texts = new()
    {
        ["firmware not supported"] = (
            "Ayar değiştirme kapalı: bu firmware desteklenmiyor (yalnızca 16Q4EMS2.107).",
            "Changing settings is turned off: this firmware is not supported (only 16Q4EMS2.107)."),
        ["layout not verified"] = (
            "Ayar değiştirme kapalı: değerler doğrulanmamış bir cihaz kaydıyla okunuyor; M-Tool'u yeniden başlatın.",
            "Changing settings is turned off: values are read with an unverified device record; restart M-Tool."),
        ["no settings backup"] = (
            "Ayar değiştirme kapalı: ilk açılışta alınan ayar yedeği bulunamadı ya da bozuk.",
            "Changing settings is turned off: the settings backup taken at first launch is missing or damaged."),
        ["lock from an earlier session"] = (
            "Ayar değiştirme kapalı: earlier session failed",
            "Changing settings is turned off: earlier session failed"),
        ["empty plan"] = ("Plan boş.", "The plan is empty."),
        ["duplicate register"] = ("0xF2 planda birden fazla kez var.", "0xF2 appears more than once in the plan."),
        ["port plan without a port"] = (
            "0xEF=0xCF: Cooler Boost ve şarj limiti bu oturumda kullanılamıyor (PawnIO erişimi yok).",
            "0xEF=0xCF: Cooler Boost and the charge limit are not available in this session (no PawnIO access)."),
        ["port register not alone"] = (
            "0xEF=0xCF: port register'ı planda tek başına olmalı.",
            "0xEF=0xCF: a port register must be the only write in its plan."),
        ["register not on the whitelist"] = ("0xF3 yazılabilir register listesinde değil.", "0xF3 is not in the list of writable registers."),
        ["cooler boost touches other bits"] = (
            "0x98=0x80: Cooler Boost yazmasında yalnızca bit 7 değişebilir (şu an 0x02).",
            "0x98=0x80: a Cooler Boost write may change only bit 7 (currently 0x02)."),
        ["unsafe fan table"] = (
            "CPU eğrisi güvensiz olur: Nokta 7: fan hızı önceki noktadan düşük olamaz.",
            "The CPU curve would be unsafe: Point 7: the fan speed cannot be lower than the previous point."),
        ["fan mode on an unsafe table"] = (
            "CPU eğrisi güvensiz olur: Nokta 7: fan hızı önceki noktadan düşük olamaz.",
            "The CPU curve would be unsafe: Point 7: the fan speed cannot be lower than the previous point."),
        ["dry run"] = ("Deneme modu: kontrol edildi, uygulanmadı.", "Dry-run mode: checked, not applied."),
        ["applied"] = ("Uygulandı ve doğrulandı.", "Applied and verified."),
        ["applied after a retry"] = ("Uygulandı ve doğrulandı.", "Applied and verified."),
        ["fan write read back differently"] = (TurkishReadBack, EnglishReadBack),
        ["factory table cannot be restored"] = (TurkishReadBack, EnglishReadBack),
        ["factory table cannot be restored, no port"] = (TurkishReadBack, EnglishReadBack),
        ["cooler boost cannot be restored"] = (TurkishReadBack, EnglishReadBack),
        ["EC access error while writing"] = ("Ayar uygulanamadı. EC hung", "The setting could not be applied. EC hung"),
        ["second plan after a failure"] = (
            $"Ayar uygulanamadı. {TurkishSecondRead}. | Ayar değiştirme kapalı: önceki bir ayar uygulanamadı (Performance mode: Balanced). Ayrıntı: {TurkishSecondRead}.",
            $"The setting could not be applied. {EnglishSecondRead}. | Changing settings is turned off: a previous setting could not be applied (Performance mode: Balanced). Detail: {EnglishSecondRead}."),
        ["EC lock busy"] = (
            TurkishUnreachable + "EC kilidi (Access_EC) 50 ms içinde alınamadı; başka bir program EC'yi kullanıyor olabilir.)",
            EnglishUnreachable + "The EC lock (Access_EC) was not obtained within 50 ms; another program may be using the EC.)"),
        ["port read fails before writing"] = (TurkishUnreachable + "EC silent)", EnglishUnreachable + "EC silent)"),
        ["port write disturbs performance mode"] = (
            "Ayar uygulanamadı. Ayar yazıldı ama başka değerler de değişti; güvenlik için durduruldu. Port erişimi başka register'ları değiştirdi: 0xF2=0xC4. Geri yüklendi: 0xF2=0xC0.",
            "The setting could not be applied. The setting was written but other values changed too; stopped for safety. The port access changed other registers: 0xF2=0xC4. Restored: 0xF2=0xC0."),
        ["port write disturbs the fan table"] = (
            "Ayar uygulanamadı. Ayar yazıldı ama başka değerler de değişti; güvenlik için durduruldu. Port erişimi başka register'ları değiştirdi: 0x6A=0x32.",
            "The setting could not be applied. The setting was written but other values changed too; stopped for safety. The port access changed other registers: 0x6A=0x32."),
    };

    public static TheoryData<string> Names() => [.. Scenarios.Select(s => s.Name)];

    private static Scenario Find(string name) => Scenarios.Single(s => s.Name == name);

    private static async Task<WriteOutcome[]> One(EcGateway gateway, WritePlan plan) =>
        [await gateway.ApplyAsync(plan)];

    private static Task<WriteRun> RunIn(string culture, Scenario scenario) =>
        WriteRig.RunAsync(culture, scenario.Act, scenario.ExceptionInMessage);

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Everything_but_the_message_is_the_same_in_every_language(string name)
    {
        var scenario = Find(name);

        var turkish = await RunIn("tr", scenario);
        var english = await RunIn("en", scenario);
        var finnish = await RunIn("fi", scenario);

        turkish.Facts.Should().NotBeEmpty();
        english.Facts.Should().Be(turkish.Facts);
        finnish.Facts.Should().Be(turkish.Facts);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task The_Turkish_text_is_the_one_from_before(string name)
    {
        var run = await RunIn("tr", Find(name));

        run.Texts.Should().Be(Texts[name].Turkish);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task The_English_text_is_English(string name)
    {
        var scenario = Find(name);

        var english = await RunIn("en", scenario);
        var finnish = await RunIn("fi", scenario);

        english.Texts.Should().Be(Texts[name].English);
        finnish.Texts.Should().Be(english.Texts);
        english.Texts.Should().NotMatchRegex("[çğıöşüÇĞİÖŞÜ]");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task The_log_is_English_in_every_language(string name)
    {
        var scenario = Find(name);

        var turkish = await RunIn("tr", scenario);

        turkish.Log.Should().NotBeEmpty();
        turkish.Log.Should().OnlyContain(line => !TurkishLetter().IsMatch(line));
    }

    [Fact]
    public async Task The_persisted_lock_reason_is_in_the_language_of_the_session_that_failed()
    {
        var turkish = await RunIn("tr", Find("fan write read back differently"));
        var english = await RunIn("en", Find("fan write read back differently"));

        turkish.LockReason.Should().StartWith("önceki bir ayar uygulanamadı (")
            .And.Contain("Ayrıntı: Ayar yazıldı ama geri okunduğunda farklı çıktı (0x72=0x");
        english.LockReason.Should().StartWith("a previous setting could not be applied (")
            .And.Contain("Detail: The setting was written but read back differently (0x72=0x");
        turkish.LockFileText.Should().EndWith(turkish.LockReason);
        english.LockFileText.Should().EndWith(english.LockReason);
    }

    [Fact]
    public async Task A_rejection_and_a_failure_are_logged_in_English_whatever_the_user_reads()
    {
        var rejected = await RunIn("tr", Find("firmware not supported"));
        var failed = await RunIn("tr", Find("fan write read back differently"));

        rejected.Log.Should().ContainSingle().Which.Should().Be(
            "WARN REJECT Charge limit: 79%: Changing settings is turned off: this firmware is not supported (only 16Q4EMS2.107).");
        failed.Log.Should().Contain(
            "ERROR Fan profile: Cool: write failed, EC writes locked. The setting was written but read back differently (0x72=0x32). ");
    }

    [Fact]
    public async Task The_scenarios_cover_every_status()
    {
        var statuses = new HashSet<string>();
        foreach (var scenario in Scenarios)
        {
            statuses.UnionWith((await RunIn("en", scenario)).Statuses);
        }

        statuses.Should().BeEquivalentTo(Enum.GetNames<WriteStatus>());
    }

    [GeneratedRegex("[çğıöşüÇĞİÖŞÜ]")]
    private static partial Regex TurkishLetter();

    private sealed record Scenario(
        string Name,
        Func<WriteRig, Task<WriteOutcome[]>> Act,
        bool ExceptionInMessage = false);
}
