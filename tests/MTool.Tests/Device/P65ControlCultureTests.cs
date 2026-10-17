using System.Text.RegularExpressions;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

/// <summary>
/// The outcomes <see cref="P65Control"/> builds itself, in Turkish, English and a language M-Tool lacks.
/// Only the text for the user may differ; the Turkish texts are the ones from before the move to resources.
/// </summary>
public sealed partial class P65ControlCultureTests
{
    private static readonly WritePolicy Live = new(FirmwareSupported: true, PreStateSaved: true, DryRun: false, PortAvailable: true);
    private static readonly WritePolicy NoPort = Live with { PortAvailable = false };

    private const string TurkishUnreachable = "Dizüstüne ulaşılamadı, hiçbir ayar değişmedi; biraz sonra yeniden deneyin (port bug).";
    private const string EnglishUnreachable = "The laptop could not be reached and no setting changed; try again shortly (port bug).";

    private static readonly Scenario[] Scenarios =
    [
        new("cooler boost without a port", async r => [await r.Control(NoPort).SetCoolerBoostAsync(true)]),
        new("cooler boost while locked", async r => [await r.Control(Live with { FirmwareSupported = false }).SetCoolerBoostAsync(true)]),
        new("cooler boost, port read fails", async r =>
        {
            r.ReadHook = register => register == EcMap.CoolerBoost ? throw new InvalidOperationException("port bug") : null;
            return [await r.Control().SetCoolerBoostAsync(true)];
        }),
        new("cooler boost applied", async r => [await r.Control().SetCoolerBoostAsync(true)]),
        new("charge limit without a port", async r => [await r.Control(NoPort).SetChargeLimitAsync(60)]),
        new("reapply, charge limit read fails", async r =>
        {
            r.ReadHook = register => register == EcMap.ChargeLimit ? throw new InvalidOperationException("port bug") : null;
            return await Desired(r.Control(), new DesiredState("Cool", ChargeLimitPercent: 60));
        }),
        new("reapply without a port", async r => await Desired(r.Control(NoPort), new DesiredState("Cool", ChargeLimitPercent: 60))),
        new("reapply stops after a failure", async r =>
        {
            r.Ec.StuckRegisters.Add(0x72);
            r.Ec[0x72] = 45;
            return await Desired(r.Control(), new DesiredState("Cool", PerformanceMode.Balanced));
        }),
        new("performance mode, dry run", async r => [await r.Control(Live with { DryRun = true }).SetPerformanceAsync(PerformanceMode.Balanced)]),
        new("fan profile applied", async r => [await r.Control().ApplyFanProfileAsync(Presets.Cool)]),
    ];

    private static readonly Dictionary<string, (string Turkish, string English)> Texts = new()
    {
        ["cooler boost without a port"] = (
            "Cooler Boost bu oturumda kullanılamıyor (PawnIO erişimi yok).",
            "Cooler Boost is not available in this session (no PawnIO access)."),
        ["cooler boost while locked"] = (
            "Ayar değiştirme kapalı: bu firmware desteklenmiyor (yalnızca 16Q4EMS2.107).",
            "Changing settings is turned off: this firmware is not supported (only 16Q4EMS2.107)."),
        ["cooler boost, port read fails"] = (TurkishUnreachable, EnglishUnreachable),
        ["cooler boost applied"] = ("Uygulandı ve doğrulandı.", "Applied and verified."),
        ["charge limit without a port"] = (
            "0xEF=0xBC: Cooler Boost ve şarj limiti bu oturumda kullanılamıyor (PawnIO erişimi yok).",
            "0xEF=0xBC: Cooler Boost and the charge limit are not available in this session (no PawnIO access)."),
        ["reapply, charge limit read fails"] = (
            "Uygulandı ve doğrulandı. | " + TurkishUnreachable,
            "Applied and verified. | " + EnglishUnreachable),
        ["reapply without a port"] = (
            "Uygulandı ve doğrulandı. | 0xEF=0xBC: Cooler Boost ve şarj limiti bu oturumda kullanılamıyor (PawnIO erişimi yok).",
            "Applied and verified. | 0xEF=0xBC: Cooler Boost and the charge limit are not available in this session (no PawnIO access)."),
        ["reapply stops after a failure"] = (
            "Ayar uygulanamadı. Ayar yazıldı ama geri okunduğunda farklı çıktı (0x72=0x32).",
            "The setting could not be applied. The setting was written but read back differently (0x72=0x32)."),
        ["performance mode, dry run"] = ("Deneme modu: kontrol edildi, uygulanmadı.", "Dry-run mode: checked, not applied."),
        ["fan profile applied"] = ("Uygulandı ve doğrulandı.", "Applied and verified."),
    };

    public static TheoryData<string> Names() => [.. Scenarios.Select(s => s.Name)];

    private static Scenario Find(string name) => Scenarios.Single(s => s.Name == name);

    private static async Task<WriteOutcome[]> Desired(P65Control control, DesiredState desired) =>
        [.. await control.ApplyDesiredAsync(desired, ProfileCatalog.BuiltIn)];

    private static Task<WriteRun> RunIn(string culture, Scenario scenario) => WriteRig.RunAsync(culture, scenario.Act);

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
    public async Task The_English_text_is_English_and_Finnish_falls_back_to_it(string name)
    {
        var english = await RunIn("en", Find(name));
        var finnish = await RunIn("fi", Find(name));

        english.Texts.Should().Be(Texts[name].English).And.NotMatchRegex("[çğıöşüÇĞİÖŞÜ]");
        finnish.Texts.Should().Be(english.Texts);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task The_log_has_no_Turkish_text(string name)
    {
        var run = await RunIn("tr", Find(name));

        run.Log.Should().OnlyContain(line => !TurkishLetter().IsMatch(line));
    }

    [Fact]
    public async Task The_scenarios_reach_the_refusal_and_the_unreachable_laptop_paths()
    {
        var statuses = new List<string>();
        foreach (var scenario in Scenarios)
        {
            statuses.AddRange((await RunIn("en", scenario)).Statuses);
        }

        statuses.Should().Contain(["Rejected", "Applied", "DryRun", "FailedRecovered"]);
    }

    [GeneratedRegex("[çğıöşüÇĞİÖŞÜ]")]
    private static partial Regex TurkishLetter();

    private sealed record Scenario(string Name, Func<WriteRig, Task<WriteOutcome[]>> Act);
}
