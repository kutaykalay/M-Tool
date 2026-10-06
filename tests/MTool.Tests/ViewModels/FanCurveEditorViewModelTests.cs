using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

public sealed class FanCurveEditorViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();
    private readonly FakeConfirm _confirm = new();
    private readonly ProfileService _service;
    private readonly StatusViewModel _status = new(new FakeNotifier(), new ImmediateDispatcher(), TimeProvider.System);
    private readonly ControlsViewModel _controls;

    public FanCurveEditorViewModelTests()
    {
        var settings = AppSettings.Default with
        {
            Desired = new DesiredState(DesiredState.DefaultProfileName),
            CustomProfiles = [new FanProfile("Gece", TestCurves.Night)],
        };
        _service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), settings, new ListLog());
        _controls = new ControlsViewModel(_service, _control, _status, new ImmediateDispatcher());
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private FanCurveEditorViewModel Editor() => new(_service, _controls, _status, _confirm, new ImmediateDispatcher());

    private static FanCurveEditorViewModel Select(FanCurveEditorViewModel editor, string name)
    {
        editor.SelectedProfile = editor.Profiles.Single(p => p.Name == name);
        return editor;
    }

    private FanCurveEditorViewModel EditorOnNight() => Select(Editor(), "Gece");

    // Brings the speed in effect at 75 °C below the 50 % envelope; each move alone is allowed.
    private static void BreakTheEnvelope(FanCurveEditorViewModel editor)
    {
        editor.MovePoint(CurveFan.Cpu, 2, 68, 45);
        editor.MovePoint(CurveFan.Cpu, 3, 75, 45);
    }

    [Fact]
    public void Opens_on_the_desired_profile_with_built_in_profiles_first()
    {
        var editor = Editor();

        editor.Profiles.Should().Equal(
            new ProfileItem("Default", IsBuiltIn: true), new ProfileItem("Cool", true), new ProfileItem("Silent", true),
            new ProfileItem("Gece", IsBuiltIn: false));
        editor.SelectedProfile!.Name.Should().Be("Default");
        editor.Draft.Should().Be(FactoryDefaults.FanCurves);
        editor.IsDirty.Should().BeFalse();
        editor.NewName.Should().Be("Default");
    }

    [Fact]
    public void A_built_in_profile_can_only_be_copied_or_applied()
    {
        var editor = Select(Editor(), "Cool");

        editor.MovePoint(CurveFan.Cpu, 6, 90, 85);

        editor.IsBuiltIn.Should().BeTrue();
        editor.Draft.Should().Be(Presets.Cool.Curves, "a built-in profile is read-only");
        editor.SaveCommand.CanExecute(null).Should().BeFalse();
        editor.RenameCommand.CanExecute(null).Should().BeFalse();
        editor.DeleteCommand.CanExecute(null).Should().BeFalse();
        editor.CopyCommand.CanExecute(null).Should().BeTrue();
        editor.ApplyCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Shows_the_points_of_the_selected_fan_with_their_limits()
    {
        var editor = EditorOnNight();

        editor.SelectedFan = CurveFan.Gpu;

        editor.Points.Select(p => (p.UpC, p.SpeedPercent)).Should().Equal(
            TestCurves.Night.Gpu.Points.Select(p => (p.UpThresholdC, p.SpeedPercent)));
        editor.Points[0].IsIdle.Should().BeTrue();
        editor.Points[6].Limits.Should().Be(CurveEditing.Limits(TestCurves.Night.Gpu, 6, FactoryDefaults.GpuDownOffsets));
    }

    [Fact]
    public void Moving_a_point_is_clamped_and_makes_the_draft_dirty_until_reverted()
    {
        var editor = EditorOnNight();

        editor.MovePoint(CurveFan.Cpu, 6, 95, 50);

        // No higher than the floor's 90 °C; no slower than the step below (85 %, above the 80 % floor).
        editor.Draft.Cpu.Points[6].Should().Be(new FanPoint(CurveValidator.SafetyFloorMaxLastThresholdC, 85));
        editor.Draft.Gpu.Should().Be(TestCurves.Night.Gpu);
        editor.IsDirty.Should().BeTrue();
        editor.HasErrors.Should().BeFalse();
        editor.Points[6].Number.Should().Be(7);
        editor.Points[6].SpeedPercent.Should().Be(85);

        editor.RevertCommand.Execute(null);

        editor.Draft.Should().Be(TestCurves.Night);
        editor.IsDirty.Should().BeFalse();
        editor.RevertCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void An_envelope_problem_is_listed_and_blocks_saving()
    {
        var editor = EditorOnNight();

        BreakTheEnvelope(editor);

        editor.Errors.Should().NotBeEmpty().And.AllSatisfy(e => e.Should().StartWith("CPU: "));
        editor.HasErrors.Should().BeTrue();
        editor.SaveCommand.CanExecute(null).Should().BeFalse();
        editor.SaveAndApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Saving_stores_the_draft_without_touching_the_ec()
    {
        var editor = EditorOnNight();
        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);

        await editor.SaveCommand.ExecuteAsync(null);

        _service.Catalog.Find("Gece")!.Curves.Cpu.Points[0].SpeedPercent.Should().Be(25);
        _control.Calls.Should().BeEmpty();
        editor.IsDirty.Should().BeFalse();
        editor.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Save_and_apply_saves_once_and_writes_the_profile_once()
    {
        var editor = EditorOnNight();
        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);

        await editor.SaveAndApplyCommand.ExecuteAsync(null);

        _service.Catalog.Find("Gece")!.Curves.Should().Be(editor.Draft);
        _control.Calls.Where(c => c.StartsWith("fan ")).Should().Equal("fan Gece");
        _control.State.FanCurves.Should().Be(editor.Draft);
        _service.Desired.FanProfile.Should().Be("Gece");
        editor.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Applying_writes_the_saved_profile()
    {
        var editor = EditorOnNight();

        await editor.ApplyCommand.ExecuteAsync(null);

        _control.Calls.Should().Contain("fan Gece");
        _controls.ActiveProfile.Should().Be("Gece");
    }

    [Fact]
    public void A_dirty_draft_must_be_saved_before_it_is_applied()
    {
        var editor = EditorOnNight();

        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);

        editor.ApplyCommand.CanExecute(null).Should().BeFalse("only saved profiles reach the EC");
        editor.SaveAndApplyCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task When_writes_are_locked_saving_still_works_but_applying_does_not()
    {
        _control.Access = _control.Access with { WriteMode = WriteMode.Locked, LockReason = "test" };
        await _controls.RefreshAsync(PortUse.None);
        var editor = EditorOnNight();

        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);

        editor.SaveCommand.CanExecute(null).Should().BeTrue();
        editor.SaveAndApplyCommand.CanExecute(null).Should().BeFalse();
        editor.ApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_write_mode_change_updates_the_apply_commands()
    {
        var editor = EditorOnNight();
        var changes = 0;
        editor.ApplyCommand.CanExecuteChanged += (_, _) => changes++;

        _control.Access = _control.Access with { WriteMode = WriteMode.Locked, LockReason = "test" };
        await _controls.RefreshAsync(PortUse.None);

        changes.Should().BePositive();
        editor.ApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_dry_run_apply_reports_dry_run()
    {
        _control.NextStatus = WriteStatus.DryRun;
        var editor = EditorOnNight();

        await editor.ApplyCommand.ExecuteAsync(null);

        _status.Message.Should().Contain("DryRun");
        editor.Message.Should().Be(_status.Message);
    }

    [Fact]
    public void Leaving_a_dirty_draft_asks_first_and_no_keeps_it()
    {
        var editor = EditorOnNight();
        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);
        var draft = editor.Draft;
        _confirm.Answer = false;
        var notified = new List<string?>();
        editor.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        Select(editor, "Cool");

        _confirm.Questions.Should().ContainSingle();
        editor.SelectedProfile!.Name.Should().Be("Gece");
        editor.Draft.Should().Be(draft);
        notified.Should().Contain(nameof(FanCurveEditorViewModel.SelectedProfile), "the list must snap back");
    }

    [Fact]
    public void Leaving_a_dirty_draft_with_yes_drops_it()
    {
        var editor = EditorOnNight();
        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);

        Select(editor, "Cool");

        _confirm.Questions.Should().ContainSingle();
        editor.Draft.Should().Be(Presets.Cool.Curves);
        editor.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Leaving_a_clean_draft_does_not_ask()
    {
        Select(EditorOnNight(), "Cool");

        _confirm.Questions.Should().BeEmpty();
    }

    [Fact]
    public void Closing_with_a_dirty_draft_asks()
    {
        var editor = EditorOnNight();
        editor.ConfirmClose().Should().BeTrue();

        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);
        _confirm.Answer = false;

        editor.ConfirmClose().Should().BeFalse();
        _confirm.Questions.Should().ContainSingle();
    }

    [Fact]
    public async Task Copying_a_profile_adds_and_selects_the_copy()
    {
        var editor = Select(Editor(), "Cool");

        await editor.CopyCommand.ExecuteAsync(null);

        editor.SelectedProfile.Should().Be(new ProfileItem("Cool kopya", IsBuiltIn: false));
        editor.Draft.Should().Be(Presets.Cool.Curves);
        _service.Catalog.Find("Cool kopya").Should().NotBeNull();
        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Renaming_keeps_the_profile_selected_under_its_new_name()
    {
        var editor = EditorOnNight();
        editor.NewName = "Uyku";

        await editor.RenameCommand.ExecuteAsync(null);

        editor.SelectedProfile.Should().Be(new ProfileItem("Uyku", IsBuiltIn: false));
        editor.Profiles.Select(p => p.Name).Should().Contain("Uyku").And.NotContain("Gece");
        editor.Draft.Should().Be(TestCurves.Night);
    }

    [Fact]
    public async Task A_rejected_rename_shows_why()
    {
        var editor = EditorOnNight();
        editor.NewName = "cool";

        await editor.RenameCommand.ExecuteAsync(null);

        editor.Message.Should().NotBeNullOrEmpty();
        editor.SelectedProfile!.Name.Should().Be("Gece");
    }

    [Fact]
    public async Task Deleting_asks_first_and_then_selects_the_desired_profile()
    {
        var editor = EditorOnNight();

        await editor.DeleteCommand.ExecuteAsync(null);

        _confirm.Questions.Should().ContainSingle().Which.Should().Contain("Gece");
        _service.Catalog.Find("Gece").Should().BeNull();
        editor.SelectedProfile!.Name.Should().Be("Default");
    }

    [Fact]
    public async Task Deleting_with_no_keeps_the_profile()
    {
        _confirm.Answer = false;
        var editor = EditorOnNight();

        await editor.DeleteCommand.ExecuteAsync(null);

        _service.Catalog.Find("Gece").Should().NotBeNull();
    }

    [Fact]
    public async Task Deleting_the_desired_profile_shows_why_it_was_refused()
    {
        await _service.SelectProfileAsync("Gece");
        var editor = Editor();

        await editor.DeleteCommand.ExecuteAsync(null);

        editor.Message.Should().Contain("seçili");
        _service.Catalog.Find("Gece").Should().NotBeNull();
    }

    [Fact]
    public async Task A_catalog_change_from_elsewhere_keeps_the_selection_and_the_draft()
    {
        var editor = EditorOnNight();
        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);
        var draft = editor.Draft;

        await _service.AddProfileAsync("Yeni", TestCurves.QuieterNight);

        editor.Profiles.Select(p => p.Name).Should().Contain("Yeni");
        editor.SelectedProfile!.Name.Should().Be("Gece");
        editor.Draft.Should().Be(draft);
        editor.IsDirty.Should().BeTrue();
    }

    // The event first is Renaming_keeps_the_profile_selected_under_its_new_name (immediate dispatcher).
    [Fact]
    public async Task A_rename_ends_on_the_new_name_when_the_command_finishes_before_the_event_is_shown()
    {
        var ui = new QueuedDispatcher();
        var editor = Select(new FanCurveEditorViewModel(_service, _controls, _status, _confirm, ui), "Gece");
        editor.NewName = "Uyku";

        await editor.RenameCommand.ExecuteAsync(null);
        ui.Pending.Should().BePositive("the catalog event is still queued");
        ui.RunAll();

        editor.SelectedProfile.Should().Be(new ProfileItem("Uyku", IsBuiltIn: false));
        editor.Profiles.Should().Contain(editor.SelectedProfile!).And.NotContain(p => p.Name == "Gece");
        editor.Draft.Should().Be(TestCurves.Night);
        editor.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Renaming_a_dirty_draft_keeps_the_draft()
    {
        var editor = EditorOnNight();
        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);
        var draft = editor.Draft;
        editor.NewName = "Uyku";

        await editor.RenameCommand.ExecuteAsync(null);

        editor.SelectedProfile!.Name.Should().Be("Uyku");
        editor.Draft.Should().Be(draft);
        editor.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void Renaming_to_the_same_name_is_disabled_but_a_case_change_is_not()
    {
        var editor = EditorOnNight();

        editor.NewName = " Gece ";
        editor.RenameCommand.CanExecute(null).Should().BeFalse();

        editor.NewName = "GECE";
        editor.RenameCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Copying_with_a_dirty_draft_asks_and_no_keeps_everything()
    {
        var editor = EditorOnNight();
        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);
        _confirm.Answer = false;

        await editor.CopyCommand.ExecuteAsync(null);

        _confirm.Questions.Should().ContainSingle();
        _service.Catalog.Find("Gece kopya").Should().BeNull();
        editor.SelectedProfile!.Name.Should().Be("Gece");
        editor.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task A_save_that_could_not_reach_the_file_shows_the_warning_and_keeps_it_after_applying()
    {
        var blocked = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocked, "");
        var settings = AppSettings.Default with { CustomProfiles = [new FanProfile("Gece", TestCurves.Night)] };
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(blocked), settings, new ListLog());
        var controls = new ControlsViewModel(service, _control, _status, new ImmediateDispatcher());
        var editor = Select(new FanCurveEditorViewModel(service, controls, _status, _confirm, new ImmediateDispatcher()), "Gece");
        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);

        await editor.SaveAndApplyCommand.ExecuteAsync(null);

        editor.Message.Should().Contain("settings.json").And.Contain("Applied");
        editor.IsDirty.Should().BeFalse("the change is kept in memory");
    }

    [Fact]
    public async Task A_rejected_apply_after_saving_shows_the_rejection_and_the_profile_stays_saved()
    {
        var editor = EditorOnNight();
        editor.MovePoint(CurveFan.Cpu, 0, 0, 25);
        _control.NextStatus = WriteStatus.Rejected;

        await editor.SaveAndApplyCommand.ExecuteAsync(null);

        editor.Message.Should().Contain("Rejected");
        _service.Catalog.Find("Gece")!.Curves.Should().Be(editor.Draft);
        _service.Desired.FanProfile.Should().Be("Default");
    }

    [Fact]
    public async Task Selecting_while_a_command_runs_is_refused_and_the_list_snaps_back_later()
    {
        _control.WriteGate = new TaskCompletionSource();
        var ui = new QueuedDispatcher();
        var editor = Select(new FanCurveEditorViewModel(_service, _controls, _status, _confirm, ui), "Gece");
        var apply = editor.ApplyCommand.ExecuteAsync(null);
        var notified = new List<string?>();
        editor.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        Select(editor, "Cool");

        editor.SelectedProfile!.Name.Should().Be("Gece");
        notified.Should().NotContain(nameof(FanCurveEditorViewModel.SelectedProfile), "the list ignores it inside its own set");
        ui.RunAll();
        notified.Should().Contain(nameof(FanCurveEditorViewModel.SelectedProfile));

        _control.WriteGate.SetResult();
        await apply;
    }

    [Fact]
    public void A_move_clamped_back_to_where_the_point_was_still_refreshes_the_points()
    {
        var editor = EditorOnNight();
        var notified = new List<string?>();
        editor.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        editor.MovePoint(CurveFan.Cpu, 6, 200, 100);

        editor.IsDirty.Should().BeFalse();
        notified.Should().Contain(nameof(FanCurveEditorViewModel.Points));
    }

    [Fact]
    public async Task Commands_are_disabled_while_an_apply_runs()
    {
        _control.WriteGate = new TaskCompletionSource();
        var editor = EditorOnNight();

        var apply = editor.ApplyCommand.ExecuteAsync(null);

        editor.IsBusy.Should().BeTrue();
        editor.ApplyCommand.CanExecute(null).Should().BeFalse();
        editor.CopyCommand.CanExecute(null).Should().BeFalse();
        editor.DeleteCommand.CanExecute(null).Should().BeFalse();

        _control.WriteGate.SetResult();
        await apply;

        editor.IsBusy.Should().BeFalse();
        editor.CopyCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task A_disposed_editor_no_longer_follows_the_catalog_or_the_write_mode()
    {
        var editor = EditorOnNight();
        var changes = 0;
        editor.ApplyCommand.CanExecuteChanged += (_, _) => changes++;

        editor.Dispose();
        await _service.AddProfileAsync("Yeni", TestCurves.QuieterNight);
        _control.Access = _control.Access with { WriteMode = WriteMode.Locked, LockReason = "test" };
        await _controls.RefreshAsync(PortUse.None);

        editor.Profiles.Select(p => p.Name).Should().NotContain("Yeni");
        changes.Should().Be(0);
    }
}
