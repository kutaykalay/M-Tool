using MTool.App.Reports;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

/// <summary>The About tab without the network (Aşama 14a): version, device rows, copy, licenses, the fixed links.</summary>
public sealed class AboutViewModelTests
{
    private static readonly SystemInfo P65System = new("MSI", "P65 Creator 9SE", null, "E16Q4IMS.10B", null, "i7-9750H", "Windows 11");

    private readonly FakeShellLauncher _shell = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeNotifier _notifier = new();
    private readonly StatusViewModel _status;
    private readonly ListLog _log = new();
    private DeviceAccess _access = new(
        new FirmwareInfo("16Q4EMS2.107", "2019-08-21"), WriteMode.Enabled, null, true, new DeviceCapabilities(false, false, false, false, []),
        new DeviceMatch(MatchKind.Exact, "p65-9se", "MSI P65 Creator 9SE"));

    private int _systemReads;
    private Func<SystemInfo> _readSystem;
    private Func<string?> _readLicenses = () => "GPL text";
    private Func<DeviceAccess>? _accessRead;

    public AboutViewModelTests()
    {
        _status = new StatusViewModel(_notifier, new ImmediateDispatcher(), TimeProvider.System);
        _readSystem = () =>
        {
            _systemReads++;
            return P65System;
        };
    }

    private AboutViewModel ViewModel(string version = "0.16.0-beta.1+448e0a5c0ffee") => new(
        version, () => (_accessRead ?? (() => _access))(), () => _readSystem(), () => new Version(2, 0, 1), () => _readLicenses(),
        _shell, _clipboard, _status, _log, @"C:\Users\x\AppData\Roaming\M-Tool\logs");

    [Theory]
    [InlineData("0.16.0-beta.1+448e0a5c0ffee", "0.16.0-beta.1 (448e0a5)")]
    [InlineData("0.16.0-beta.1+448e0a5", "0.16.0-beta.1 (448e0a5)")]
    [InlineData("0.16.0", "0.16.0")]
    [InlineData("0.16.0+", "0.16.0")]
    [InlineData("0.16.0+not-a-commit", "0.16.0")]
    [InlineData("no version", "no version")]
    public void The_version_drops_the_build_metadata_but_keeps_a_short_commit(string informational, string shown)
    {
        ViewModel(informational).VersionText.Should().Be(string.Format(Strings.About_Version, shown));
    }

    [Fact]
    public void Before_loading_there_are_no_device_rows_and_nothing_was_read()
    {
        ViewModel().DeviceLines.Should().BeEmpty();
        _systemReads.Should().Be(0);
    }

    [Fact]
    public async Task Loading_fills_the_device_rows()
    {
        var vm = ViewModel();

        await vm.LoadAsync();

        vm.DeviceLines.Should().Equal(DeviceInfo.Lines(P65System, _access, new Version(2, 0, 1)));
    }

    [Fact]
    public async Task SMBIOS_is_read_once_but_the_write_mode_is_shown_as_it_is_now()
    {
        var vm = ViewModel();
        await vm.LoadAsync();
        _access = _access with { WriteMode = WriteMode.Locked, LockReason = "write failed" };

        await vm.LoadAsync();

        _systemReads.Should().Be(1);
        vm.DeviceLines.Single(l => l.Label == Strings.About_Device_Writes).Value.Should().Be(Strings.About_Writes_Locked);
    }

    [Fact]
    public async Task When_SMBIOS_cannot_be_read_the_rows_say_unknown_and_the_log_has_the_reason()
    {
        _readSystem = () => throw new System.Management.ManagementException("WMI broke");
        var vm = ViewModel();

        await vm.LoadAsync();

        vm.DeviceLines.Single(l => l.Label == Strings.About_Device_Model).Value.Should().Be(Strings.About_Unknown);
        vm.DeviceLines.Single(l => l.Label == Strings.About_Device_Firmware).Value.Should().Be("16Q4EMS2.107 (2019-08-21)");
        _log.Lines.Should().ContainSingle(l => l.StartsWith("WARN") && l.Contains("SMBIOS"));
    }

    [Fact]
    public async Task Copy_puts_the_version_and_the_rows_on_the_clipboard()
    {
        var vm = ViewModel();
        await vm.LoadAsync();

        vm.CopyCommand.Execute(null);

        _clipboard.Texts.Should().Equal(DeviceInfo.ToText("0.16.0-beta.1 (448e0a5)", vm.DeviceLines));
        _status.Message.Should().Be(Strings.About_Copied);
    }

    [Fact]
    public async Task A_busy_clipboard_is_a_warning_not_a_crash()
    {
        var vm = ViewModel();
        await vm.LoadAsync();
        _clipboard.Fails = true;

        vm.CopyCommand.Execute(null);

        _status.Message.Should().Be(Strings.About_CopyFailed);
        _log.Lines.Should().ContainSingle(l => l.StartsWith("WARN") && l.Contains("clipboard"));
    }

    [Fact]
    public void Copy_waits_for_the_rows()
    {
        ViewModel().CopyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void The_links_open_the_fixed_project_pages_and_the_log_folder()
    {
        var vm = ViewModel();

        vm.OpenGitHubCommand.Execute(null);
        vm.OpenReleasesCommand.Execute(null);
        vm.OpenLogFolderCommand.Execute(null);

        _shell.Urls.Should().Equal(ProjectLinks.Repository, ProjectLinks.Releases);
        _shell.Folders.Should().Equal(@"C:\Users\x\AppData\Roaming\M-Tool\logs");
    }

    [Fact]
    public void A_shell_failure_is_a_warning_without_the_path()
    {
        _shell.Fails = true;
        var vm = ViewModel();

        vm.OpenLogFolderCommand.Execute(null);

        _status.Message.Should().Be(Strings.Report_OpenFailed);
        _status.Message.Should().NotContain(@"C:\");
    }

    [Fact]
    public void The_licenses_are_read_once_when_first_shown()
    {
        var reads = 0;
        _readLicenses = () =>
        {
            reads++;
            return "GPL text";
        };
        var vm = ViewModel();

        reads.Should().Be(0);
        vm.LicenseText.Should().Be("GPL text");
        _ = vm.LicenseText;
        reads.Should().Be(1);
    }

    [Fact]
    public void Missing_license_texts_point_to_the_GitHub_files()
    {
        _readLicenses = () => null;

        ViewModel().LicenseText.Should().Be(Strings.About_LicensesUnavailable);
    }

    [Fact]
    public void A_license_read_that_throws_is_logged_and_points_to_the_GitHub_files()
    {
        _readLicenses = () => throw new IOException("resource stream");

        ViewModel().LicenseText.Should().Be(Strings.About_LicensesUnavailable);
        _log.Lines.Should().ContainSingle(l => l.StartsWith("WARN") && l.Contains("license"));
    }

    [Fact]
    public async Task A_load_that_fails_is_logged_and_the_next_one_tries_again()
    {
        var vm = ViewModel();
        var fail = true;
        var access = _access;
        _accessRead = () => fail ? throw new InvalidOperationException("session closed") : access;

        await vm.LoadAsync();
        fail = false;
        await vm.LoadAsync();

        _log.Lines.Should().ContainSingle(l => l.StartsWith("WARN") && l.Contains("device rows"));
        vm.DeviceLines.Should().NotBeEmpty();
    }

    private sealed class FakeClipboard : IClipboard
    {
        public List<string> Texts { get; } = [];

        public bool Fails { get; set; }

        public void SetText(string text)
        {
            if (Fails)
            {
                throw new System.Runtime.InteropServices.COMException("OpenClipboard failed", unchecked((int)0x800401D0));
            }

            Texts.Add(text);
        }
    }
}
