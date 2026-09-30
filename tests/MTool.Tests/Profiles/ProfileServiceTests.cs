using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Profiles;

public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();
    private readonly ListLog _log = new();
    private readonly List<DesiredState> _changes = [];

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ProfileService Service(AppSettings? settings = null, string? folder = null)
    {
        var service = new ProfileService(
            _control, ProfileCatalog.BuiltIn, new SettingsStore(folder ?? _folder), settings ?? AppSettings.Default, _log);
        service.DesiredChanged += _changes.Add;
        return service;
    }

    private AppSettings Saved() => new SettingsStore(_folder).Load().Settings;

    private bool SettingsFileExists => File.Exists(Path.Combine(_folder, "settings.json"));

    [Theory]
    [InlineData(WriteStatus.Applied)]
    [InlineData(WriteStatus.DryRun)]
    public async Task A_written_profile_becomes_the_desired_one_and_is_saved(WriteStatus status)
    {
        _control.NextStatus = status;
        var service = Service();

        var result = await service.SelectProfileAsync("cool");

        result.Outcome.Status.Should().Be(status);
        result.SaveWarning.Should().BeNull();
        _control.Calls.Should().Equal("fan Cool");
        service.Desired.FanProfile.Should().Be("Cool");
        Saved().Desired.FanProfile.Should().Be("Cool");
        _changes.Should().Equal(service.Desired);
    }

    [Theory]
    [InlineData(WriteStatus.Rejected)]
    [InlineData(WriteStatus.FailedRecovered)]
    [InlineData(WriteStatus.FailedUnrecovered)]
    public async Task A_write_that_did_not_happen_changes_nothing(WriteStatus status)
    {
        _control.NextStatus = status;
        var service = Service();

        var result = await service.SelectProfileAsync("Cool");

        result.Outcome.Status.Should().Be(status);
        service.Desired.Should().Be(DesiredState.Default);
        SettingsFileExists.Should().BeFalse();
        _changes.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_profile_is_rejected_without_touching_the_ec()
    {
        var result = await Service().SelectProfileAsync("Turbo");

        result.Outcome.Status.Should().Be(WriteStatus.Rejected);
        result.Outcome.Message.Should().Contain("Turbo");
        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Performance_mode_and_charge_limit_are_kept_in_the_desired_state()
    {
        var service = Service(AppSettings.Default with { Desired = new DesiredState("Silent") });

        await service.SetPerformanceAsync(PerformanceMode.Eco);
        await service.SetChargeLimitAsync(60);

        service.Desired.Should().Be(new DesiredState("Silent", PerformanceMode.Eco, 60));
        Saved().Desired.Should().Be(service.Desired);
        _control.Calls.Should().Equal("performance Eco", "charge 60");
    }

    [Theory]
    [InlineData(49)]
    [InlineData(101)]
    public async Task An_out_of_range_charge_limit_is_rejected_without_touching_the_ec(int percent)
    {
        var result = await Service().SetChargeLimitAsync(percent);

        result.Outcome.Status.Should().Be(WriteStatus.Rejected);
        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Cooler_boost_is_not_saved()
    {
        var service = Service();

        var result = await service.SetCoolerBoostAsync(true);

        result.Outcome.Status.Should().Be(WriteStatus.Applied);
        _control.Calls.Should().Equal("boost True");
        service.Desired.Should().Be(DesiredState.Default);
        SettingsFileExists.Should().BeFalse();
    }

    [Fact]
    public async Task Commands_run_one_at_a_time_in_order()
    {
        _control.WriteGate = new TaskCompletionSource();
        var service = Service();

        var first = service.SelectProfileAsync("Cool");
        var second = service.SetPerformanceAsync(PerformanceMode.Balanced);
        await Task.Delay(50);
        service.IsBusy.Should().BeTrue();
        _control.Calls.Should().Equal("fan Cool");

        _control.WriteGate.SetResult();
        await Task.WhenAll(first, second);

        _control.Calls.Should().Equal("fan Cool", "performance Balanced");
        _control.MaxWritesRunning.Should().Be(1);
        service.IsBusy.Should().BeFalse();
        service.Desired.Should().Be(new DesiredState("Cool", PerformanceMode.Balanced));
    }

    [Fact]
    public async Task Reapply_writes_the_desired_state_and_does_not_save()
    {
        var desired = new DesiredState("Cool", PerformanceMode.High);
        var service = Service(AppSettings.Default with { Desired = desired });

        var outcomes = await service.ReapplyAsync();

        outcomes.Should().ContainSingle().Which.Status.Should().Be(WriteStatus.Applied);
        _control.Calls.Should().Equal($"desired {desired}");
        SettingsFileExists.Should().BeFalse();
    }

    [Fact]
    public async Task A_settings_file_that_cannot_be_written_is_a_warning_not_a_crash()
    {
        var blocked = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocked, "");
        var service = Service(folder: blocked);

        var result = await service.SelectProfileAsync("Cool");

        result.Outcome.Status.Should().Be(WriteStatus.Applied);
        result.SaveWarning.Should().Contain("settings.json");
        service.Desired.FanProfile.Should().Be("Cool");
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR"));
    }

    [Fact]
    public async Task Other_settings_are_kept_when_the_desired_state_is_saved()
    {
        var service = Service(AppSettings.Default with { DryRun = false });

        await service.SelectProfileAsync("Cool");

        Saved().DryRun.Should().BeFalse();
    }

    [Fact]
    public async Task A_throwing_subscriber_does_not_hide_the_result()
    {
        var service = Service();
        service.DesiredChanged += _ => throw new InvalidOperationException("UI gone");

        var result = await service.SelectProfileAsync("Cool");

        result.Outcome.Status.Should().Be(WriteStatus.Applied);
        service.Desired.FanProfile.Should().Be("Cool");
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("UI gone"));
    }

    [Fact]
    public async Task Unsanitized_settings_never_reach_the_ec()
    {
        var service = Service(AppSettings.Default with { Desired = new DesiredState("Turbo", ChargeLimitPercent: 120) });

        await service.ReapplyAsync();

        service.Desired.Should().Be(DesiredState.Default);
        _control.Calls.Should().Equal($"desired {DesiredState.Default}");
    }
}
