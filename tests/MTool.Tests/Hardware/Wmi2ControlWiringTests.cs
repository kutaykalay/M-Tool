using System.Text.RegularExpressions;
using MTool.Tests.Fakes;

namespace MTool.Tests.Hardware;

/// <summary>
/// Where experimental control is wired (plan asama15 15c-5c): only the reports-only WMI2 window builds it, and the
/// P65 path never reads the consent, so a consent in settings.json cannot change what the P65 window does.
/// </summary>
public class Wmi2ControlWiringTests
{
    [Fact]
    public void Only_the_consent_store_reads_the_experimental_consent_in_the_app()
    {
        var readers = RepoFiles.Under(Path.Combine("src", "MTool.App"), "*.cs")
            .Where(file => File.ReadAllText(file).Contains("ExperimentalWmi2"))
            .Select(Path.GetFileName);

        readers.Should().BeEquivalentTo(["Wmi2Consent.cs"], "the P65 window and its services never look at the consent");
    }

    [Fact]
    public void Control_is_opened_and_its_consent_saved_only_from_the_start_up_of_the_wmi2_window()
    {
        Users("new SettingsWmi2Consent(").Should().BeEquivalentTo(["GuiBootstrapper.cs"]);
        Users("Wmi2ControlSource.OpenAsync(").Should().BeEquivalentTo(["GuiBootstrapper.cs"]);
        Users("new Wmi2ControlsViewModel(").Should().BeEquivalentTo(["GuiBootstrapper.cs"]);
    }

    [Fact]
    public void The_start_up_builds_control_only_inside_the_reports_only_path()
    {
        var reportsOnly = Method(Bootstrapper, "private static void ShowReportsOnly(");
        var p65 = Method(Bootstrapper, "private static async Task<GuiBootstrapper> BuildAsync(");

        reportsOnly.Should().Contain("new Wmi2ControlsViewModel(");
        p65.Should().NotContain("Wmi2Control").And.NotContain("ShowReportsOnly(");
        Regex.Matches(Bootstrapper, @"\bWmi2Control\(settings,").Should().HaveCount(1, "built once, for the reports-only window");
        Method(Bootstrapper, "private static async Task<EcSession?> OpenSessionAsync(")
            .Should().MatchRegex(@"ReportsOnly is \{ \} reportsOnly\)[\s\S]{0,400}?Wmi2Control\(settings,");
    }

    [Fact]
    public void The_wmi2_snapshot_lives_in_the_folder_of_the_p65_one_so_it_gets_the_same_permissions()
    {
        // Neither store sets its own ACL: both files inherit the permissions of AppPaths.Root.
        Bootstrapper.Should().MatchRegex(@"WriteAccessBootstrap\.CreateAsync\(\s*session\.Worker,\s*session\.Layout,\s*AppPaths\.Root,");
        Bootstrapper.Should().MatchRegex(@"Wmi2ControlSource\.OpenAsync\(\s*firmware,\s*list,\s*AppPaths\.Root,");
        RepoFiles.Under("src", "*.cs").Select(File.ReadAllText).Should().NotContain(c => c.Contains("SetAccessControl"));
    }

    private static string Bootstrapper => File.ReadAllText(Path.Combine(RepoFiles.Root, "src", "MTool.App", "Startup", "GuiBootstrapper.cs"));

    /// <summary>From the signature to the next member at class level (four spaces of indent).</summary>
    private static string Method(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, signature);
        var end = Regex.Match(code[(start + signature.Length)..], @"\n    (private|public|internal) ").Index;
        return end > 0 ? code.Substring(start, signature.Length + end) : code[start..];
    }

    private static IEnumerable<string?> Users(string text) =>
        RepoFiles.Under("src", "*.cs").Where(file => File.ReadAllText(file).Contains(text)).Select(Path.GetFileName);
}
