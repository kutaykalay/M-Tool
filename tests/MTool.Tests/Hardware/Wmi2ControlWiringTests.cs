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
        var code = File.ReadAllText(Path.Combine(RepoFiles.Root, "src", "MTool.App", "Startup", "GuiBootstrapper.cs"));
        var reportsOnly = code.IndexOf("private static void ShowReportsOnly(", StringComparison.Ordinal);

        reportsOnly.Should().BePositive();
        code.IndexOf("new Wmi2ControlsViewModel(", StringComparison.Ordinal).Should().BeGreaterThan(reportsOnly);
        code.IndexOf("BuildAsync(", StringComparison.Ordinal).Should().BeLessThan(reportsOnly, "the P65 window is built before, and apart from, it");
    }

    [Fact]
    public void The_wmi2_snapshot_lives_in_the_folder_of_the_p65_one_so_it_gets_the_same_permissions()
    {
        // Neither store sets its own ACL: both files inherit the permissions of AppPaths.Root.
        var code = File.ReadAllText(Path.Combine(RepoFiles.Root, "src", "MTool.App", "Startup", "GuiBootstrapper.cs")).ReplaceLineEndings("\n");

        code.Should().Contain("WriteAccessBootstrap.CreateAsync(\n            session.Worker, session.Layout, AppPaths.Root,");
        code.Should().Contain("Wmi2ControlSource.OpenAsync(firmware, list, AppPaths.Root,");
        RepoFiles.Under("src", "*.cs").Select(File.ReadAllText).Should().NotContain(c => c.Contains("SetAccessControl"));
    }

    private static IEnumerable<string?> Users(string text) =>
        RepoFiles.Under("src", "*.cs").Where(file => File.ReadAllText(file).Contains(text)).Select(Path.GetFileName);
}
