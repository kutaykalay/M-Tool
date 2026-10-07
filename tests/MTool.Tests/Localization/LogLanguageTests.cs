using System.Globalization;
using System.Text.RegularExpressions;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Localization;

/// <summary>The log is English in every UI language, so a log attached to an issue reads the same for everyone.</summary>
public sealed partial class LogLanguageTests
{
    private static readonly WritePolicy Live = new(FirmwareSupported: true, PreStateSaved: true, DryRun: false, PortAvailable: true);

    [Fact]
    public async Task Gateway_log_lines_are_the_same_English_text_in_every_ui_language()
    {
        var turkish = await GatewayLogIn("tr-TR");
        var english = await GatewayLogIn("en");
        var german = await GatewayLogIn("de");

        turkish.Should().NotBeEmpty();
        english.Should().Equal(turkish);
        german.Should().Equal(turkish);
        turkish.Should().OnlyContain(line => !TurkishLetter().IsMatch(line));
    }

    [Fact]
    public void Log_calls_in_the_source_carry_no_Turkish_text()
    {
        var offenders = SourceFiles()
            .SelectMany(file => LogCall().Matches(File.ReadAllText(file))
                .Where(call => TurkishLetter().IsMatch(call.Value))
                .Select(call => $"{Path.GetFileName(file)}: {call.Value.Split('\n')[0].Trim()}"))
            .ToList();

        // One string, so a failure lists every offending call rather than the first.
        string.Join(Environment.NewLine, offenders).Should().BeEmpty();
    }

    private static async Task<IReadOnlyList<string>> GatewayLogIn(string culture)
    {
        var (ui, format) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var log = new ListLog();
            using var worker = new EcWorker(P65Memory.FactorySnapshot(), new FakeEcLock(), TimeSpan.FromMilliseconds(50));
            var gateway = new EcGateway(worker, Live, log);

            await gateway.ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));
            await gateway.ApplyAsync(WritePlans.Performance(PerformanceMode.High));
            await gateway.ApplyAsync(WritePlans.Fan(FanMode.Advanced));
            await new EcGateway(worker, Live with { DryRun = true }, log).ApplyAsync(WritePlans.ChargeLimit(80));

            return [.. log.Lines];
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, format);
        }
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MTool.slnx")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the tests run inside the repository");
        return Directory.EnumerateFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    // A call on an IAppLog (log, _log, or a lambda's log) up to the first ";". A guard against new
    // Turkish log text, not a proof: it does not see a log method passed as a delegate (log.Warn into
    // ReportCommand, warnings.ForEach(log.Warn)), text that arrives in a variable (the warnings that the
    // window shows too, which get an English twin when they move to the resources), Turkish words
    // without a Turkish letter, or text after a ";" inside a literal.
    [GeneratedRegex(@"\b_?log\s*\.\s*(?:Info|Warn|Error)\s*\([^;]*;", RegexOptions.IgnoreCase)]
    private static partial Regex LogCall();

    [GeneratedRegex("[çğıöşüÇĞİÖŞÜ]")]
    private static partial Regex TurkishLetter();
}
