using Microsoft.Extensions.Time.Testing;
using MTool.App.Reports;
using MTool.App.Resources;
using MTool.App.Updates;
using MTool.App.ViewModels;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Core.Updates;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

/// <summary>The update part of the About tab (Aşama 14b): only a notice and a link, the exe never changes itself.</summary>
public sealed class UpdatesViewModelTests : IDisposable
{
    private static readonly SemVersion Newer = SemVersion.Parse("0.17.0-beta.1");

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeUpdateClient _client = new();
    private readonly FakeShellLauncher _shell = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeTimeProvider _time = new();
    private readonly QueuedDispatcher _ui = new();
    private readonly ListLog _log = new();
    private readonly StatusViewModel _status;
    private ProfileService? _service;

    public UpdatesViewModelTests() => _status = new StatusViewModel(_notifier, new ImmediateDispatcher(), TimeProvider.System);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private UpdatesViewModel ViewModel(bool autoCheck = false, bool knownVersion = true, CancellationToken stop = default, string? folder = null)
    {
        _service = new ProfileService(new FakeP65Control(), ProfileCatalog.BuiltIn, new SettingsStore(folder ?? _folder),
            AppSettings.Default with { AutoUpdateCheck = autoCheck }, new ListLog());
        return new UpdatesViewModel(knownVersion ? _client : null, _service, _status, _notifier, _shell, _ui, _log, _time, stop);
    }

    [Fact]
    public async Task A_release_page_kept_from_an_elevated_browser_says_the_address_was_copied()
    {
        _client.Next = new UpdateCheckOutcome(UpdateStatus.UpdateAvailable, 200, Newer);
        _shell.UrlFailure = new BrowserNotOpenedException(addressCopied: false);
        var vm = ViewModel();
        await vm.CheckCommand.ExecuteAsync(null);

        vm.OpenReleasePageCommand.Execute(null);

        _status.Message.Should().Be(Strings.Shell_UrlNotOpened);
    }

    [Fact]
    public async Task A_check_with_a_newer_release_says_so_and_offers_its_page()
    {
        _client.Next = new UpdateCheckOutcome(UpdateStatus.UpdateAvailable, 200, Newer);
        var vm = ViewModel();

        await vm.CheckCommand.ExecuteAsync(null);

        vm.StatusText.Should().Be(string.Format(Strings.Update_Available, "0.17.0-beta.1"));
        vm.OpenReleasePageCommand.CanExecute(null).Should().BeTrue();
        vm.OpenReleasePageCommand.Execute(null);
        _shell.Urls.Should().Equal(ProjectLinks.ReleaseTag(Newer));
        _log.Lines.Should().ContainSingle(l => l.StartsWith("INFO") && l.Contains("UpdateAvailable") && l.Contains("0.17.0-beta.1"));
    }

    [Theory]
    [InlineData(UpdateStatus.UpToDate, "Update_UpToDate")]
    [InlineData(UpdateStatus.CurrentIsNewer, "Update_CurrentIsNewer")]
    [InlineData(UpdateStatus.Unavailable, "Update_Unreadable")]
    [InlineData(UpdateStatus.InvalidResponse, "Update_Unreadable")]
    [InlineData(UpdateStatus.Network, "Update_Unreachable")]
    [InlineData(UpdateStatus.Timeout, "Update_Timeout")]
    public async Task Every_outcome_has_its_text_and_no_page(UpdateStatus status, string key)
    {
        _client.Next = new UpdateCheckOutcome(status, Latest: status is UpdateStatus.UpToDate or UpdateStatus.CurrentIsNewer ? Newer : null);
        var vm = ViewModel();

        await vm.CheckCommand.ExecuteAsync(null);

        vm.StatusText.Should().Be(Strings.ResourceManager.GetString(key));
        vm.OpenReleasePageCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_rate_limit_says_how_many_minutes_to_wait_and_keeps_the_button_off_that_long()
    {
        _client.Next = new UpdateCheckOutcome(UpdateStatus.RateLimited, 403, RetryAfter: TimeSpan.FromSeconds(301));
        var vm = ViewModel();

        await vm.CheckCommand.ExecuteAsync(null);

        vm.StatusText.Should().Be(string.Format(Strings.Update_RateLimited, 6));
        _time.Advance(TimeSpan.FromMinutes(5));
        _ui.RunAll();
        vm.CheckCommand.CanExecute(null).Should().BeFalse();
        _time.Advance(TimeSpan.FromSeconds(2));
        _ui.RunAll();
        vm.CheckCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task After_a_check_the_button_rests_for_a_minute()
    {
        _client.Next = new UpdateCheckOutcome(UpdateStatus.UpToDate, 200, Newer);
        var vm = ViewModel();
        var changes = 0;
        vm.CheckCommand.CanExecuteChanged += (_, _) => changes++;

        await vm.CheckCommand.ExecuteAsync(null);

        vm.CheckCommand.CanExecute(null).Should().BeFalse();
        _time.Advance(UpdatesViewModel.Rest - TimeSpan.FromSeconds(1));
        _ui.RunAll();
        vm.CheckCommand.CanExecute(null).Should().BeFalse();
        _time.Advance(TimeSpan.FromSeconds(1));
        _ui.RunAll();
        vm.CheckCommand.CanExecute(null).Should().BeTrue();
        changes.Should().BeGreaterThan(0, "the button must hear that it may run again");
    }

    [Fact]
    public async Task A_running_check_cannot_be_started_twice()
    {
        var answer = new TaskCompletionSource<UpdateCheckOutcome>();
        _client.Pending = answer.Task;
        var vm = ViewModel();

        var first = vm.CheckCommand.ExecuteAsync(null);

        vm.IsChecking.Should().BeTrue();
        vm.StatusText.Should().Be(Strings.Update_Checking);
        vm.CheckCommand.CanExecute(null).Should().BeFalse();
        answer.SetResult(new UpdateCheckOutcome(UpdateStatus.UpToDate, 200, Newer));
        await first;
        vm.IsChecking.Should().BeFalse();
        _client.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_client_that_throws_does_not_leave_the_button_stuck()
    {
        _client.Throws = new InvalidOperationException("bug");
        var vm = ViewModel();

        await vm.CheckCommand.ExecuteAsync(null);

        vm.IsChecking.Should().BeFalse();
        vm.StatusText.Should().Be(Strings.Update_Unreadable);
        _log.Lines.Should().ContainSingle(l => l.StartsWith("ERROR") && l.Contains("Update check"));
        _time.Advance(UpdatesViewModel.Rest);
        _ui.RunAll();
        vm.CheckCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task A_startup_check_whose_balloon_fails_is_only_logged()
    {
        _client.Next = new UpdateCheckOutcome(UpdateStatus.UpdateAvailable, 200, Newer);
        _notifier.Fails = true;
        var vm = ViewModel(autoCheck: true);

        var run = vm.RunStartupCheckAsync();
        _time.Advance(UpdatesViewModel.StartupDelay);
        var finish = async () => await run;

        await finish.Should().NotThrowAsync();
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("start-up update check"));
    }

    [Fact]
    public async Task A_cancelled_check_says_nothing()
    {
        _client.Next = UpdateCheckOutcome.WithoutAnswer(UpdateStatus.Cancelled);
        var vm = ViewModel();

        await vm.CheckCommand.ExecuteAsync(null);

        vm.StatusText.Should().BeNull();
    }

    [Fact]
    public void Without_a_known_version_nothing_can_be_checked()
    {
        var vm = ViewModel(knownVersion: false);

        vm.CheckCommand.CanExecute(null).Should().BeFalse();
        vm.StatusText.Should().Be(Strings.Update_UnknownVersion);
    }

    [Fact]
    public async Task The_automatic_check_is_off_by_default_and_never_asks()
    {
        var vm = ViewModel();

        await vm.RunStartupCheckAsync();

        vm.AutoCheck.Should().BeFalse();
        _client.Calls.Should().Be(0);
    }

    [Fact]
    public async Task When_on_the_startup_check_waits_a_minute_then_asks_once()
    {
        _client.Next = new UpdateCheckOutcome(UpdateStatus.UpToDate, 200, Newer);
        var vm = ViewModel(autoCheck: true);

        var run = vm.RunStartupCheckAsync();
        _time.Advance(UpdatesViewModel.StartupDelay - TimeSpan.FromSeconds(1));
        _client.Calls.Should().Be(0);
        _time.Advance(TimeSpan.FromSeconds(1));
        await run;

        _client.Calls.Should().Be(1);
        _notifier.Infos.Should().BeEmpty("only a newer release is worth a balloon");
    }

    [Fact]
    public async Task A_newer_release_found_at_startup_is_a_balloon()
    {
        _client.Next = new UpdateCheckOutcome(UpdateStatus.UpdateAvailable, 200, Newer);
        var vm = ViewModel(autoCheck: true);

        var run = vm.RunStartupCheckAsync();
        _time.Advance(UpdatesViewModel.StartupDelay);
        await run;

        _notifier.Infos.Should().ContainSingle().Which.Message.Should().Be(string.Format(Strings.Update_AvailableBalloon, "0.17.0-beta.1"));
        vm.OpenReleasePageCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_startup_check_is_only_logged()
    {
        _client.Next = UpdateCheckOutcome.WithoutAnswer(UpdateStatus.Network);
        var vm = ViewModel(autoCheck: true);

        var run = vm.RunStartupCheckAsync();
        _time.Advance(UpdatesViewModel.StartupDelay);
        await run;

        _notifier.Infos.Should().BeEmpty();
        _notifier.Errors.Should().BeEmpty();
        _log.Lines.Should().ContainSingle(l => l.Contains("Network"));
    }

    [Fact]
    public async Task Closing_M_Tool_during_the_startup_wait_asks_nothing()
    {
        using var stop = new CancellationTokenSource();
        var vm = ViewModel(autoCheck: true, stop: stop.Token);

        var run = vm.RunStartupCheckAsync();
        await stop.CancelAsync();
        await run;

        _client.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Turning_the_automatic_check_on_saves_it()
    {
        var vm = ViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.ToggleAutoCheckCommand.ExecuteAsync(null);

        vm.AutoCheck.Should().BeTrue();
        new SettingsStore(_folder).Load().Settings.AutoUpdateCheck.Should().BeTrue();
        changed.Should().Contain(nameof(UpdatesViewModel.AutoCheck));
        _client.Calls.Should().Be(0, "turning it on is not a check");
    }

    [Fact]
    public async Task Turning_the_automatic_check_off_during_the_startup_wait_asks_nothing()
    {
        var vm = ViewModel(autoCheck: true);

        var run = vm.RunStartupCheckAsync();
        await vm.ToggleAutoCheckCommand.ExecuteAsync(null);
        _time.Advance(UpdatesViewModel.StartupDelay);
        await run;

        _client.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_setting_that_cannot_be_saved_is_a_warning_and_stays_for_this_session()
    {
        var blocked = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocked, "");
        var vm = ViewModel(folder: blocked);

        await vm.ToggleAutoCheckCommand.ExecuteAsync(null);

        _status.MessageKind.Should().Be(MessageKind.Warning);
        _status.Message.Should().Contain("settings.json");
        vm.AutoCheck.Should().BeTrue();
    }

    private sealed class FakeUpdateClient : IUpdateClient
    {
        public UpdateCheckOutcome Next { get; set; } = new(UpdateStatus.UpToDate, 200);

        public Task<UpdateCheckOutcome>? Pending { get; set; }

        public Exception? Throws { get; set; }

        public int Calls { get; private set; }

        public Task<UpdateCheckOutcome> CheckAsync(CancellationToken stop)
        {
            Calls++;
            return Throws is { } error ? Task.FromException<UpdateCheckOutcome>(error) : Pending ?? Task.FromResult(Next);
        }
    }
}
