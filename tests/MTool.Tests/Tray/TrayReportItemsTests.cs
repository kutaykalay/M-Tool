using System.Windows.Forms;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Reports;
using MTool.App.Tray;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Tray;

/// <summary>The report part of the tray menu (Aşama 8, G2).</summary>
public sealed class TrayReportItemsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeConfirm _confirm = new();
    private readonly FakeShellLauncher _shell = new();
    private readonly ListLog _log = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ReportViewModel ViewModel(FakeReportSources sources) =>
        new(new ReportService(sources, _log, Path.Combine(_root, "reports"), "0.9.0", []), _confirm, _shell,
            new StatusViewModel(_notifier, new ImmediateDispatcher(), TimeProvider.System), _notifier, _log);

    private static string[] Texts(ReportViewModel vm)
    {
        var items = TrayReportItems.For(vm);
        try
        {
            return [.. items.Select(i => i.Text ?? "")];
        }
        finally
        {
            Dispose(items);
        }
    }

    private static void Dispose(IEnumerable<ToolStripItem> items)
    {
        foreach (var item in items)
        {
            item.Dispose();
        }
    }

    private static async Task Click(ReportViewModel vm, string text)
    {
        var items = TrayReportItems.For(vm);
        try
        {
            ((ToolStripMenuItem)items.Single(i => i.Text == text)).PerformClick();
            await Task.WhenAll(
                ((IAsyncRelayCommand)vm.CreateCommand).ExecutionTask ?? Task.CompletedTask,
                ((IAsyncRelayCommand)vm.CreateWithWmi2Command).ExecutionTask ?? Task.CompletedTask);
        }
        finally
        {
            Dispose(items);
        }
    }

    [Fact]
    public async Task A_wmi1_laptop_has_the_report_and_the_issue_item_and_no_wmi2_item()
    {
        var vm = ViewModel(new FakeReportSources());
        await vm.LoadAsync();

        Texts(vm).Should().Equal("Cihaz raporu oluştur", "Sorun bildir (GitHub)");
    }

    [Fact]
    public async Task A_wmi2_laptop_also_has_the_separate_wmi2_item_after_the_plain_one()
    {
        var vm = ViewModel(new FakeReportSources { Wmi = WmiInterface.Wmi2 });
        await vm.LoadAsync();

        Texts(vm).Should().Equal("Cihaz raporu oluştur", "Cihaz raporu (WMI2 dahil)", "Sorun bildir (GitHub)");
    }

    [Fact]
    public void Until_the_laptop_is_known_there_is_no_wmi2_item()
    {
        var vm = ViewModel(new FakeReportSources { Wmi = WmiInterface.Wmi2 });

        Texts(vm).Should().NotContain("Cihaz raporu (WMI2 dahil)");
    }

    [Fact]
    public async Task The_folder_item_is_there_only_when_there_is_a_last_report()
    {
        var vm = ViewModel(new FakeReportSources());
        Texts(vm).Should().NotContain("Son raporun klasörünü aç");

        await Click(vm, "Cihaz raporu oluştur");

        Texts(vm).Should().Equal("Cihaz raporu oluştur", "Son raporun klasörünü aç", "Sorun bildir (GitHub)");
    }

    [Fact]
    public async Task Clicking_the_main_item_makes_a_wmi1_report_and_never_calls_wmi2()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };
        var vm = ViewModel(sources);
        await vm.LoadAsync();

        await Click(vm, "Cihaz raporu oluştur");

        sources.Wmi2Reads.Should().Be(0);
        vm.LastReportPath.Should().NotBeNull();
        _confirm.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task Clicking_the_wmi2_item_asks_first_and_a_no_means_no_call_at_all()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };
        var vm = ViewModel(sources);
        await vm.LoadAsync();
        _confirm.Answer = false;

        await Click(vm, "Cihaz raporu (WMI2 dahil)");

        _confirm.Questions.Should().ContainSingle();
        sources.Wmi2Reads.Should().Be(0);
        vm.LastReportPath.Should().BeNull();
    }

    [Fact]
    public async Task Clicking_the_wmi2_item_and_agreeing_reads_wmi2_once()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };
        var vm = ViewModel(sources);
        await vm.LoadAsync();

        await Click(vm, "Cihaz raporu (WMI2 dahil)");

        sources.Wmi2Reads.Should().Be(1);
    }

    [Fact]
    public async Task The_issue_item_opens_the_form_only_when_clicked()
    {
        var vm = ViewModel(new FakeReportSources());
        await Click(vm, "Cihaz raporu oluştur");
        _shell.Urls.Should().BeEmpty();

        await Click(vm, "Sorun bildir (GitHub)");

        _shell.Urls.Should().ContainSingle().Which.Host.Should().Be("github.com");
    }

    [Fact]
    public async Task The_folder_item_opens_the_reports_folder()
    {
        var vm = ViewModel(new FakeReportSources());
        await Click(vm, "Cihaz raporu oluştur");

        await Click(vm, "Son raporun klasörünü aç");

        _shell.Folders.Should().Equal(Path.Combine(_root, "reports"));
    }

    [Fact]
    public async Task While_a_report_runs_the_report_items_are_disabled()
    {
        using var gate = new ManualResetEventSlim();
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2, Gate = gate };
        var vm = ViewModel(sources);
        await vm.LoadAsync();
        vm.CreateCommand.Execute(null);
        sources.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        var items = TrayReportItems.For(vm);
        try
        {
            items.Where(i => i.Text is "Cihaz raporu oluştur" or "Cihaz raporu (WMI2 dahil)").Should().OnlyContain(i => !i.Enabled);
            items.Single(i => i.Text == "Sorun bildir (GitHub)").Enabled.Should().BeTrue();
        }
        finally
        {
            Dispose(items);
            gate.Set();
            await (((IAsyncRelayCommand)vm.CreateCommand).ExecutionTask ?? Task.CompletedTask);
        }
    }

    [Fact]
    public async Task With_writing_locked_the_report_items_stay_enabled_and_the_ec_is_never_touched()
    {
        _control.Access = _control.Access with { WriteMode = WriteMode.Locked };
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_root), AppSettings.Default, _log);
        var controls = new ControlsViewModel(service, _control,
            new StatusViewModel(_notifier, new ImmediateDispatcher(), TimeProvider.System), new ImmediateDispatcher());
        await controls.RefreshAsync(PortUse.None); // the one EC read of this test: it makes the lock known
        _control.Calls.Clear();
        var vm = ViewModel(new FakeReportSources { Wmi = WmiInterface.Wmi2 });
        await vm.LoadAsync();
        // Not TrayDeviceItems: its performance sub menu makes WinForms install its synchronization context on this thread.
        controls.CanWrite.Should().BeFalse();
        controls.CanWritePort.Should().BeFalse();

        var items = TrayReportItems.For(vm);
        try
        {
            items.Should().OnlyContain(i => i.Enabled);
            ((ToolStripMenuItem)items[0]).PerformClick();
            await (((IAsyncRelayCommand)vm.CreateCommand).ExecutionTask ?? Task.CompletedTask);
        }
        finally
        {
            Dispose(items);
        }

        _control.Calls.Should().BeEmpty();
        vm.LastReportPath.Should().NotBeNull();
    }

    [Fact]
    public async Task The_texts_follow_the_interface_language()
    {
        using var _ = TestCulture.Use("en");
        var vm = ViewModel(new FakeReportSources { Wmi = WmiInterface.Wmi2 });
        await vm.LoadAsync();

        Texts(vm).Should().Equal("Create device report", "Device report (with WMI2)", "Report an issue (GitHub)");
    }
}
