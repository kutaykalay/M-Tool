using System.Globalization;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Sensors;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

/// <summary>
/// The view model texts in English (the rest of the view model tests run in Turkish). The protected
/// phrases are what the user types or looks for, so they must survive every language.
/// </summary>
public sealed class ViewModelTextTests : IDisposable
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly DateTimeOffset At = new(2026, 9, 30, 13, 5, 9, TimeSpan.Zero);

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();
    private readonly FakeNotifier _notifier = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static void InEnglish(Action test)
    {
        var (ui, culture) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        CultureInfo.CurrentUICulture = English;
        CultureInfo.CurrentCulture = English;
        try
        {
            test();
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, culture);
        }
    }

    private StatusViewModel Status() => new(_notifier, new ImmediateDispatcher(), TimeProvider.System);

    private static CommandResult Result(WriteStatus status) => new(new WriteOutcome(status, [], "msg"));

    private static DeviceAccess Access(WriteMode mode, FirmwareInfo? firmware = null) =>
        new(firmware, mode, "reason", PortFeaturesAvailable: true, TestLayouts.P65.Capabilities);

    // --- status ---

    [Fact]
    public void The_dry_run_band_names_settings_json_and_the_dryRun_switch_in_English()
    {
        InEnglish(() =>
        {
            var status = Status();

            status.SetAccess(Access(WriteMode.DryRun, FakeP65Control.SupportedFirmware));

            status.AccessBanner.Should().Contain("settings.json").And.Contain("\"dryRun\": false").And.NotContain("Deneme");
        });
    }

    [Fact]
    public void A_recovered_failure_tells_the_user_the_unlock_command_in_English()
    {
        InEnglish(() =>
        {
            var status = Status();

            status.Report(Result(WriteStatus.FailedRecovered));

            status.Message.Should().Contain("M-Tool.exe --unlock --confirm").And.NotContain("yönetici");
            status.MessageKind.Should().Be(MessageKind.Error);
            _notifier.Errors.Should().ContainSingle().Which.Title.Should().Be("M-Tool: setting could not be applied");
        });
    }

    [Fact]
    public void An_unrecovered_failure_names_Cooler_Boost_in_English()
    {
        InEnglish(() =>
        {
            var status = Status();

            status.Report(Result(WriteStatus.FailedUnrecovered));

            // The write may have failed too: the text says it was tried, never that it worked.
            status.Message.Should().Contain("an attempt was made to switch Cooler Boost on").And.NotContain("was switched on").And.NotContain("Bilgisayarı");
        });
    }

    [Fact]
    public void A_locked_session_says_monitor_only_in_English()
    {
        InEnglish(() =>
        {
            var status = Status();

            status.SetAccess(Access(WriteMode.Locked, new FirmwareInfo("16Q4EMS2.999", "")));

            status.AccessBanner.Should().Contain("16Q4EMS2.999").And.Contain("monitor-only").And.NotContain("izleme");
        });
    }

    [Fact]
    public void Drift_parts_are_joined_with_the_separator_of_the_language()
    {
        InEnglish(() =>
        {
            var status = Status();

            status.SetDrift(new StateDrift(true, true, true, true), dryRun: false);

            status.DriftText.Should().Contain("fan table, performance mode, charge limit, fan mode").And.Contain("Reapply");
        });
    }

    [Fact]
    public void The_separator_follows_the_language_of_the_window_not_the_regional_format()
    {
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = English;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var status = Status();

            status.SetDrift(new StateDrift(true, false, true, false), dryRun: false);

            status.DriftText.Should().Contain("fan table, charge limit");
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, culture);
        }
    }

    // --- controls ---

    private ControlsViewModel Controls()
    {
        var noChoices = AppSettings.Default with { Desired = new DesiredState(DesiredState.DefaultProfileName) };
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), noChoices, new ListLog());
        return new ControlsViewModel(service, _control, Status(), new ImmediateDispatcher());
    }

    [Fact]
    public async Task The_charge_label_is_a_percent_in_English()
    {
        var controls = Controls();
        _control.State = _control.State with { Port = new PortState(0x82, 0xD0) };
        await controls.RefreshAsync(PortUse.Allowed);

        InEnglish(() =>
        {
            controls.ChargeLimitLabel.Should().Be("80%");
            controls.ChargeLimitDraftLabel.Should().Be("80%");
        });
    }

    [Fact]
    public async Task An_unknown_performance_value_and_fan_table_read_in_English()
    {
        var controls = Controls();
        _control.State = _control.State with { PerformanceRaw = 0x00, Performance = null, FanCurves = TestCurves.Night };
        await controls.RefreshAsync(PortUse.None);

        InEnglish(() =>
        {
            controls.PerformanceLabel.Should().Be("factory setting (0x00)");
            controls.ActiveProfileLabel.Should().Be("Unrecognized fan setting");
        });
    }

    [Fact]
    public void Performance_button_names_come_from_Texts_Mode()
    {
        InEnglish(() =>
        {
            var controls = Controls();

            controls.PerformanceOptions.Should().NotBeEmpty();
            controls.PerformanceOptions.Should().OnlyContain(o => o.Name == Texts.Mode(o.Mode));
            controls.PerformanceOptions.Select(o => o.Name).Should().BeSubsetOf(["High", "Balanced", "Eco"]);
        });
    }

    // --- main ---

    [Theory]
    [InlineData(ReapplyTrigger.Startup, "Settings could not be applied at startup: msg")]
    [InlineData(ReapplyTrigger.Resume, "Settings could not be applied after waking from sleep: msg")]
    [InlineData(ReapplyTrigger.PowerSource, "Settings could not be applied after the power source changed: msg")]
    [InlineData(ReapplyTrigger.Retry, "Settings could not be applied on retry: msg")]
    public async Task Every_automatic_reapply_trigger_gives_a_full_sentence_in_English(ReapplyTrigger trigger, string expected)
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var log = new ListLog();
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), AppSettings.Default, log);
        using var poller = new SensorPoller(_control.ReadSensorsAsync, () => true, () => service.IsBusy, time, log, MainViewModel.HiddenInterval);
        var main = new MainViewModel(poller, service, _control, new FakePowerSource(), _notifier, new ImmediateDispatcher(), time);
        var result = new AutoReapplyResult(trigger, [new WriteOutcome(WriteStatus.Rejected, [], "msg")]);
        var (ui, culture) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        CultureInfo.CurrentUICulture = English;
        try
        {
            await main.OnAutoReappliedAsync(result);

            main.Status.Message.Should().Be(expected);
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, culture);
        }
    }

    // --- sensors ---

    [Theory]
    [InlineData("en-US", "1:05:09 PM")]
    [InlineData("tr-TR", "13:05:09")]
    public void The_stale_time_uses_the_long_time_format_of_the_window_language_not_the_regional_format(string cultureName, string time)
    {
        var (ui, culture) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        try
        {
            var sensors = new SensorsViewModel();
            var local = new DateTimeOffset(2026, 9, 30, 13, 5, 9, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 30)));

            sensors.Apply(new SensorReading(null, SensorStatus.Stale, 3, local));

            sensors.Freshness.Should().EndWith($"({time})");
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, culture);
        }
    }

    [Fact]
    public void The_other_sensor_states_read_in_English()
    {
        InEnglish(() =>
        {
            var sensors = new SensorsViewModel();
            sensors.Freshness.Should().Be("Reading…");

            sensors.Apply(new SensorReading(null, SensorStatus.Paused, 0, At));
            sensors.Freshness.Should().Be("Paused (sleep)");

            sensors.Apply(new SensorReading(null, SensorStatus.Stale, 3, null));
            sensors.Freshness.Should().Be("No data");
        });
    }

    // --- power switch ---

    [Fact]
    public void The_power_source_line_reads_in_English()
    {
        InEnglish(() =>
        {
            var source = new FakePowerSource { Current = PowerSource.Battery };
            var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), AppSettings.Default, new ListLog());
            var vm = new PowerSwitchViewModel(service, source, Status(), new ImmediateDispatcher());

            vm.SourceLabel.Should().Be("Now: on battery.");
            source.Set(PowerSource.Ac);
            vm.SourceLabel.Should().Be("Now: on AC power.");
        });
    }

    // --- sign-in start ---

    [Fact]
    public async Task The_sign_in_repair_warning_is_English_in_the_log_and_localized_in_the_band()
    {
        var task = new FakeStartupTask { RegisteredExe = @"C:\Users\PC\Downloads\M-Tool.exe" };
        var log = new ListLog();
        var status = Status();
        var vm = new SignInStartViewModel(task, @"C:\Program Files\M-Tool\M-Tool.exe", status, log);
        var (ui, culture) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        CultureInfo.CurrentUICulture = English;
        try
        {
            await vm.LoadAsync();

            status.Message.Should().Contain("Start at sign-in is not the expected task").And.Contain("another exe");
            log.Lines.Should().ContainSingle().Which.Should().Contain("Start at sign-in task is not the expected one");
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, culture);
        }
    }

    [Fact]
    public async Task The_sign_in_repair_log_line_is_the_same_in_every_language()
    {
        var lines = new List<string>();
        foreach (var language in new[] { "en", "tr" })
        {
            var log = new ListLog();
            var task = new FakeStartupTask { RegisteredExe = @"C:\Users\PC\Downloads\M-Tool.exe" };
            var (ui, culture) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            try
            {
                await new SignInStartViewModel(task, @"C:\Program Files\M-Tool\M-Tool.exe", Status(), log).LoadAsync();
                lines.Add(log.Lines.Single());
            }
            finally
            {
                (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, culture);
            }
        }

        lines[1].Should().Be(lines[0]);
    }

    // --- editor ---

    [Fact]
    public async Task The_editor_save_hint_names_the_apply_button_in_English()
    {
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), AppSettings.Default, new ListLog());
        var controls = new ControlsViewModel(service, _control, Status(), new ImmediateDispatcher());
        await service.AddProfileAsync("Mine", TestCurves.Night);
        using var editor = new FanCurveEditorViewModel(service, controls, Status(), new FakeConfirm(), new ImmediateDispatcher());
        editor.SelectedProfile = editor.Profiles.Single(p => p.Name == "Mine");
        editor.MovePoint(CurveFan.Cpu, 0, 60, 60);
        var (ui, culture) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        CultureInfo.CurrentUICulture = English;
        try
        {
            editor.SaveCommand.CanExecute(null).Should().BeTrue();
            await editor.SaveCommand.ExecuteAsync(null);

            editor.Message.Should().Be("Saved. Press Apply to use it on the fans.");
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, culture);
        }
    }
}
