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
        service.Desired.Should().Be(DesiredState.Default with { FanProfile = "Cool", Performance = PerformanceMode.Balanced });
    }

    [Fact]
    public async Task Reapply_writes_the_desired_state_and_does_not_save()
    {
        var desired = new DesiredState("Cool", PerformanceMode.High);
        var service = Service(AppSettings.Default with { Desired = desired });

        var outcomes = await service.ReapplyAsync(PortUse.Allowed);

        outcomes.Should().ContainSingle().Which.Status.Should().Be(WriteStatus.Applied);
        _control.Calls.Should().Equal($"desired {desired}");
        SettingsFileExists.Should().BeFalse();
    }

    [Fact]
    public async Task A_saved_custom_profile_is_written_back_on_reapply_and_can_be_selected()
    {
        var night = new FanProfile("Gece", Presets.Silent.Curves with
        {
            Cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)),
        });
        var service = Service(AppSettings.Default with { Desired = new DesiredState("Gece"), CustomProfiles = [night] });

        await service.ReapplyAsync(PortUse.None);
        _control.State = _control.State with { FanCurves = FactoryDefaults.FanCurves };
        var selected = await service.SelectProfileAsync("gece");

        service.Desired.FanProfile.Should().Be("Gece");
        selected.Outcome.Status.Should().Be(WriteStatus.Applied);
        _control.State.FanCurves.Should().Be(night.Curves);
    }

    [Fact]
    public async Task A_dropped_custom_profile_is_not_reapplied()
    {
        var broken = new FanProfile("Gece", null!);
        var service = Service(AppSettings.Default with { Desired = new DesiredState("Gece"), CustomProfiles = [broken] });

        await service.ReapplyAsync(PortUse.None);

        service.Desired.FanProfile.Should().Be("Default");
        _control.State.FanCurves.Should().Be(FactoryDefaults.FanCurves);
    }

    [Fact]
    public async Task Reapply_without_the_port_leaves_the_charge_limit_out_and_keeps_the_desired_state()
    {
        var desired = new DesiredState("Cool", PerformanceMode.High, ChargeLimitPercent: 60);
        var service = Service(AppSettings.Default with { Desired = desired });

        var outcomes = await service.ReapplyAsync(PortUse.None);

        outcomes.Should().ContainSingle().Which.Status.Should().Be(WriteStatus.Applied);
        _control.Calls.Should().Equal($"desired {desired with { ChargeLimitPercent = null }}");
        service.Desired.Should().Be(desired);
        SettingsFileExists.Should().BeFalse();
        _changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Reapply_waits_for_a_running_command_and_counts_as_busy()
    {
        _control.WriteGate = new TaskCompletionSource();
        var service = Service();

        var command = service.SelectProfileAsync("Cool");
        SpinWait.SpinUntil(() => !_control.Calls.IsEmpty, TimeSpan.FromSeconds(5)).Should().BeTrue("the command's write started");
        var reapply = service.ReapplyAsync(PortUse.None);

        reapply.IsCompleted.Should().BeFalse();
        _control.Calls.Should().Equal("fan Cool");
        service.IsBusy.Should().BeTrue();

        _control.WriteGate.SetResult();
        await Task.WhenAll(command, reapply);

        _control.Calls.Should().HaveCount(2).And.HaveElementAt(1, $"desired {(DesiredState.Default with { FanProfile = "Cool" }).WithoutPortParts()}");
        _control.MaxWritesRunning.Should().Be(1);
        service.IsBusy.Should().BeFalse();
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

        await service.ReapplyAsync(PortUse.Allowed);

        var dropped = new DesiredState("Default");
        service.Desired.Should().Be(dropped);
        _control.Calls.Should().Equal($"desired {dropped}");
    }

    // --- custom profiles ---

    private static readonly FanCurves NightCurves = Presets.Silent.Curves with
    {
        Cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)),
    };

    private static readonly FanCurves QuieterNightCurves = Presets.Silent.Curves with
    {
        Cpu = FanCurve.Of((0, 25), (60, 40), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)),
    };

    // The last step below the 80 % floor.
    private static readonly FanCurves UnsafeCurves = Presets.Silent.Curves with
    {
        Cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 75), (85, 85), (90, 70)),
    };

    private static AppSettings WithNight(string desired = "Default") => AppSettings.Default with
    {
        Desired = new DesiredState(desired),
        CustomProfiles = [new FanProfile("Gece", NightCurves)],
    };

    [Fact]
    public async Task Library_changes_are_saved_and_never_touch_the_ec()
    {
        var service = Service();

        (await service.AddProfileAsync("Gece", NightCurves)).Should().Be(new ProfileCommandResult(null));
        Saved().CustomProfiles.Should().Equal(new FanProfile("Gece", NightCurves));

        (await service.SaveCurvesAsync("gece", QuieterNightCurves)).Error.Should().BeNull();
        Saved().CustomProfiles.Should().Equal(new FanProfile("Gece", QuieterNightCurves));

        (await service.RenameProfileAsync("Gece", "Sessiz gece")).Error.Should().BeNull();
        Saved().CustomProfiles.Should().Equal(new FanProfile("Sessiz gece", QuieterNightCurves));

        (await service.DeleteProfileAsync("Sessiz gece")).Error.Should().BeNull();
        Saved().CustomProfiles.Should().BeEmpty();

        _control.Calls.Should().BeEmpty();
        service.Catalog.Profiles.Should().Equal(ProfileCatalog.BuiltIn.Profiles);
        service.Desired.Should().Be(DesiredState.Default);
        _changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_library_change_publishes_a_new_catalog()
    {
        var service = Service();
        var before = service.Catalog;

        await service.AddProfileAsync("Gece", NightCurves);

        before.Profiles.Should().Equal(ProfileCatalog.BuiltIn.Profiles, "a catalog already handed out never changes");
        service.Catalog.Should().NotBeSameAs(before);
        service.Catalog.Find("gece")!.Curves.Should().Be(NightCurves);
    }

    public static TheoryData<string, Func<ProfileService, Task<ProfileCommandResult>>> RejectedChanges => new()
    {
        { "built-in name", s => s.AddProfileAsync("default", NightCurves) },
        { "unsafe curve", s => s.AddProfileAsync("Yeni", UnsafeCurves) },
        { "rename a built-in", s => s.RenameProfileAsync("Cool", "Serin") },
        { "rename to a taken name", s => s.RenameProfileAsync("Gece", "Silent") },
        { "delete the desired profile", s => s.DeleteProfileAsync("Gece") },
        { "delete an unknown profile", s => s.DeleteProfileAsync("Turbo") },
        { "save an unsafe curve", s => s.SaveCurvesAsync("Gece", UnsafeCurves) },
        { "save a built-in", s => s.SaveCurvesAsync("Silent", NightCurves) },
    };

    [Theory]
    [MemberData(nameof(RejectedChanges))]
    public async Task A_rejected_library_change_changes_nothing(string why, Func<ProfileService, Task<ProfileCommandResult>> change)
    {
        var settings = WithNight(desired: "Gece");
        var service = Service(settings);
        var catalog = service.Catalog;
        var catalogChanges = 0;
        service.CatalogChanged += () => catalogChanges++;

        var result = await change(service);

        result.Error.Should().NotBeNullOrWhiteSpace(why);
        result.SaveWarning.Should().BeNull();
        service.Catalog.Should().BeSameAs(catalog);
        service.Desired.Should().Be(settings.Desired);
        SettingsFileExists.Should().BeFalse();
        catalogChanges.Should().Be(0);
        _changes.Should().BeEmpty();
        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Catalog_changed_is_raised_once_per_change()
    {
        var service = Service();
        var catalogChanges = 0;
        service.CatalogChanged += () => catalogChanges++;

        await service.AddProfileAsync("Gece", NightCurves);
        await service.AddProfileAsync("Gece", NightCurves);

        catalogChanges.Should().Be(1, "the second add is a duplicate name");
    }

    [Fact]
    public async Task A_throwing_catalog_subscriber_is_logged_and_does_not_hide_the_result()
    {
        var service = Service();
        service.CatalogChanged += () => throw new InvalidOperationException("UI gone");

        var result = await service.AddProfileAsync("Gece", NightCurves);

        result.Error.Should().BeNull();
        service.Catalog.Find("Gece").Should().NotBeNull();
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("UI gone"));
    }

    [Fact]
    public async Task Renaming_the_desired_profile_moves_the_desired_state_without_an_ec_write()
    {
        var service = Service(WithNight(desired: "Gece"));

        var result = await service.RenameProfileAsync("Gece", "Uyku");

        result.Error.Should().BeNull();
        service.Desired.FanProfile.Should().Be("Uyku");
        Saved().Desired.FanProfile.Should().Be("Uyku");
        service.Catalog.Find(service.Desired.FanProfile).Should().NotBeNull();
        _changes.Should().Equal(service.Desired);
        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_library_change_that_cannot_be_saved_is_kept_in_memory_with_a_warning()
    {
        var blocked = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocked, "");
        var service = Service(folder: blocked);
        var catalogChanges = 0;
        service.CatalogChanged += () => catalogChanges++;

        var result = await service.AddProfileAsync("Gece", NightCurves);

        result.Error.Should().BeNull();
        result.SaveWarning.Should().Contain("settings.json");
        service.Catalog.Find("Gece").Should().NotBeNull();
        catalogChanges.Should().Be(1);
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR"));
    }

    [Fact]
    public async Task An_added_profile_can_be_selected_and_is_written_with_its_curves()
    {
        var service = Service();
        await service.AddProfileAsync("Gece", NightCurves);

        var result = await service.SelectProfileAsync("gece");

        result.Outcome.Status.Should().Be(WriteStatus.Applied);
        _control.Calls.Should().Equal("fan Gece");
        _control.State.FanCurves.Should().Be(NightCurves);
        service.Desired.FanProfile.Should().Be("Gece");
        Saved().Desired.FanProfile.Should().Be("Gece");
    }

    [Fact]
    public async Task Reapply_writes_the_curves_saved_last_for_the_desired_profile()
    {
        var service = Service(WithNight(desired: "Gece"));
        await service.ReapplyAsync(PortUse.None);

        await service.SaveCurvesAsync("Gece", QuieterNightCurves);
        _control.State.FanCurves.Should().Be(NightCurves, "saving does not write the EC");
        await service.ReapplyAsync(PortUse.None);

        _control.State.FanCurves.Should().Be(QuieterNightCurves);
    }

    [Fact]
    public async Task A_selection_waiting_for_the_lock_never_saves_a_name_that_was_renamed_meanwhile()
    {
        _control.WriteGate = new TaskCompletionSource();
        var service = Service(WithNight());

        var running = service.SelectProfileAsync("Cool");
        SpinWait.SpinUntil(() => !_control.Calls.IsEmpty, TimeSpan.FromSeconds(5)).Should().BeTrue("the first write started");
        // Queued in this order (SemaphoreSlim serves async waiters first in, first out). Were that ever
        // to change, the selection would run first and be Applied: the test fails, it never passes falsely.
        var rename = service.RenameProfileAsync("Gece", "Uyku");
        var select = service.SelectProfileAsync("Gece");
        _control.WriteGate.SetResult();
        await Task.WhenAll(running, rename, select);

        (await rename).Error.Should().BeNull();
        (await select).Outcome.Status.Should().Be(WriteStatus.Rejected);
        (await select).Outcome.Message.Should().Contain("Gece");
        _control.Calls.Should().Equal("fan Cool");
        service.Desired.FanProfile.Should().Be("Cool");
        service.Catalog.Find(Saved().Desired.FanProfile).Should().NotBeNull();
    }

    [Fact]
    public async Task A_library_change_waits_for_a_running_write()
    {
        _control.WriteGate = new TaskCompletionSource();
        var service = Service();

        var command = service.SelectProfileAsync("Cool");
        SpinWait.SpinUntil(() => !_control.Calls.IsEmpty, TimeSpan.FromSeconds(5)).Should().BeTrue("the write started");
        var add = service.AddProfileAsync("Gece", NightCurves);
        await Task.Delay(50);

        add.IsCompleted.Should().BeFalse();
        service.Catalog.Find("Gece").Should().BeNull();

        _control.WriteGate.SetResult();
        await Task.WhenAll(command, add);

        Saved().Desired.FanProfile.Should().Be("Cool");
        Saved().CustomProfiles.Should().ContainSingle().Which.Name.Should().Be("Gece");
    }

    [Fact]
    public async Task A_reader_on_another_thread_always_sees_a_whole_catalog()
    {
        var service = Service();
        using var stop = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var reader = Task.Run(() =>
        {
            var seen = 0;
            started.Set();
            while (!stop.IsCancellationRequested)
            {
                var names = service.Catalog.Profiles.Select(p => p.Name).ToList();
                names.Should().OnlyHaveUniqueItems().And.StartWith(ProfileCatalog.BuiltIn.Profiles.Select(p => p.Name));
                seen++;
            }

            return seen;
        });
        started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("the reader runs while the profiles change");

        for (var i = 1; i <= ProfileCatalog.MaxCustomProfiles; i++)
        {
            (await service.AddProfileAsync($"Profil {i}", NightCurves)).Error.Should().BeNull();
        }

        await stop.CancelAsync();
        (await reader).Should().BePositive();
        service.Catalog.Profiles.Should().HaveCount(ProfileCatalog.BuiltIn.Profiles.Count + ProfileCatalog.MaxCustomProfiles);
    }
}
