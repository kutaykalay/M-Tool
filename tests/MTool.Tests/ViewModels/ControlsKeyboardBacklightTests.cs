using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

/// <summary>
/// The keyboard backlight bar: shows what the EC holds, writes once per click or finished drag,
/// and follows the Fn key while the window is open. Never part of the desired state.
/// </summary>
public sealed class ControlsKeyboardBacklightTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();
    private readonly ProfileService _service;
    private readonly ControlsViewModel _controls;

    public ControlsKeyboardBacklightTests()
    {
        _service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), AppSettings.Default, new ListLog());
        _controls = Controls(_control);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ControlsViewModel Controls(IP65Control control) =>
        new(_service, control, new StatusViewModel(new FakeNotifier(), new ImmediateDispatcher(), TimeProvider.System), new ImmediateDispatcher());

    private IEnumerable<string> BacklightWrites => _control.Calls.Where(c => c.StartsWith("keyboard backlight", StringComparison.Ordinal));

    private void EcHolds(int? level, byte raw) => _control.State = _control.State with { KeyboardBacklight = new(level, raw) };

    [Fact]
    public void The_bar_is_shown_when_the_record_has_a_keyboard_backlight()
    {
        _controls.ShowKeyboardBacklight.Should().BeTrue();
        _controls.KeyboardBacklightMax.Should().Be(3);
    }

    [Fact]
    public void The_bar_is_hidden_on_a_model_without_one()
    {
        _control.Access = _control.Access with { Capabilities = _control.Access.Capabilities with { KeyboardBacklight = false } };

        Controls(_control).ShowKeyboardBacklight.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0x80, "Kapalı")]
    [InlineData(1, 0x81, "1")]
    [InlineData(3, 0x83, "3")]
    public async Task A_refresh_shows_the_ec_level_without_writing(int level, byte raw, string label)
    {
        EcHolds(level, raw);

        await _controls.RefreshAsync(PortUse.None);

        _controls.KeyboardBacklightLevel.Should().Be(level);
        _controls.KeyboardBacklightKnown.Should().BeTrue();
        _controls.KeyboardBacklightLabel.Should().Be(label);
        BacklightWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_byte_selects_no_step_and_names_the_byte()
    {
        EcHolds(null, 0x84);

        await _controls.RefreshAsync(PortUse.None);

        _controls.KeyboardBacklightKnown.Should().BeFalse();
        _controls.KeyboardBacklightLabel.Should().Contain("0x84");
        BacklightWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Choosing_a_step_writes_it_once_and_shows_the_ec_after()
    {
        await _controls.RefreshAsync(PortUse.None);

        _controls.KeyboardBacklightLevel = 3;

        BacklightWrites.Should().Equal("keyboard backlight 3");
        _controls.KeyboardBacklightLevel.Should().Be(3);
        _controls.KeyboardBacklightLabel.Should().Be("3");
    }

    [Fact]
    public async Task The_step_the_ec_already_holds_is_not_written_again()
    {
        await _controls.RefreshAsync(PortUse.None);

        _controls.KeyboardBacklightLevel = 1;

        BacklightWrites.Should().BeEmpty();
    }

    // A click is a press and release (Begin/End): it writes even when the bar already sits on that dot.
    [Fact]
    public async Task After_an_unknown_byte_a_click_on_any_dot_writes_it()
    {
        EcHolds(null, 0x84);
        await _controls.RefreshAsync(PortUse.None);

        _controls.BeginKeyboardBacklightDrag();
        _controls.KeyboardBacklightLevel = 0;
        _controls.EndKeyboardBacklightDrag();

        BacklightWrites.Should().Equal("keyboard backlight 0");
    }

    // The window ends a press on both the button release and the lost mouse capture.
    [Fact]
    public async Task A_second_release_or_one_without_a_press_writes_nothing()
    {
        await _controls.RefreshAsync(PortUse.None);
        var gate = new TaskCompletionSource();
        _control.WriteGate = gate;

        _controls.EndKeyboardBacklightDrag();
        _controls.BeginKeyboardBacklightDrag();
        _controls.KeyboardBacklightLevel = 3;
        _controls.EndKeyboardBacklightDrag();
        _controls.EndKeyboardBacklightDrag();

        _controls.KeyboardBacklightLevel.Should().Be(3, "the second release must not snap the bar back while the write runs");
        gate.SetResult();
        BacklightWrites.Should().Equal("keyboard backlight 3");
    }

    [Fact]
    public async Task A_refresh_during_a_drag_does_not_move_the_bar()
    {
        await _controls.RefreshAsync(PortUse.None);
        _controls.BeginKeyboardBacklightDrag();
        _controls.KeyboardBacklightLevel = 3;

        await _controls.RefreshAsync(PortUse.None);

        _controls.KeyboardBacklightLevel.Should().Be(3);
        _controls.EndKeyboardBacklightDrag();
        BacklightWrites.Should().Equal("keyboard backlight 3");
    }

    [Fact]
    public async Task A_drag_writes_only_the_step_it_ends_on()
    {
        await _controls.RefreshAsync(PortUse.None);

        _controls.BeginKeyboardBacklightDrag();
        _controls.KeyboardBacklightLevel = 2;
        _controls.KeyboardBacklightLevel = 3;
        BacklightWrites.Should().BeEmpty();
        _controls.EndKeyboardBacklightDrag();

        BacklightWrites.Should().Equal("keyboard backlight 3");
    }

    [Fact]
    public async Task A_drag_back_to_the_ec_step_writes_nothing()
    {
        await _controls.RefreshAsync(PortUse.None);

        _controls.BeginKeyboardBacklightDrag();
        _controls.KeyboardBacklightLevel = 3;
        _controls.KeyboardBacklightLevel = 1;
        _controls.EndKeyboardBacklightDrag();

        BacklightWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refused_write_snaps_back_to_the_ec_step()
    {
        await _controls.RefreshAsync(PortUse.None);
        _control.NextStatus = WriteStatus.Rejected;

        _controls.KeyboardBacklightLevel = 3;

        BacklightWrites.Should().Equal("keyboard backlight 3");
        _controls.KeyboardBacklightLevel.Should().Be(1);
    }

    [Fact]
    public async Task Locked_writes_leave_the_bar_on_the_ec_step()
    {
        _control.Access = _control.Access with { WriteMode = WriteMode.Locked, LockReason = "test" };
        await _controls.RefreshAsync(PortUse.None);

        _controls.KeyboardBacklightLevel = 3;

        _controls.CanWrite.Should().BeFalse();
        BacklightWrites.Should().BeEmpty();
        _controls.KeyboardBacklightLevel.Should().Be(1);
    }

    // The bar is disabled while a write runs (CanWrite); a value that still arrives is not queued.
    [Fact]
    public async Task A_step_chosen_while_another_write_runs_is_dropped_and_the_bar_shows_the_ec()
    {
        await _controls.RefreshAsync(PortUse.None);
        var gate = new TaskCompletionSource();
        _control.WriteGate = gate;

        _controls.KeyboardBacklightLevel = 2;
        _controls.CanWrite.Should().BeFalse();
        _controls.KeyboardBacklightLevel = 3;
        gate.SetResult();
        await _controls.RefreshAsync(PortUse.None);

        BacklightWrites.Should().Equal("keyboard backlight 2");
        _controls.KeyboardBacklightLevel.Should().Be(2);
    }

    [Fact]
    public async Task Following_the_fn_key_reads_only_the_backlight()
    {
        await _controls.RefreshAsync(PortUse.None);
        _control.Calls.Clear();
        EcHolds(3, 0x83);

        await _controls.FollowKeyboardBacklightAsync();

        _control.Calls.Should().Equal("read backlight");
        _controls.KeyboardBacklightLevel.Should().Be(3);
        BacklightWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Following_waits_while_a_write_runs()
    {
        await _controls.RefreshAsync(PortUse.None);
        var gate = new TaskCompletionSource();
        _control.WriteGate = gate;
        _controls.KeyboardBacklightLevel = 2;
        _control.Calls.Clear();

        await _controls.FollowKeyboardBacklightAsync();

        _control.Calls.Should().BeEmpty();
        gate.SetResult();
    }

    [Fact]
    public async Task Following_waits_while_the_bar_is_dragged()
    {
        await _controls.RefreshAsync(PortUse.None);
        _controls.BeginKeyboardBacklightDrag();
        _controls.KeyboardBacklightLevel = 3;
        _control.Calls.Clear();

        await _controls.FollowKeyboardBacklightAsync();

        _control.Calls.Should().BeEmpty();
        _controls.KeyboardBacklightLevel.Should().Be(3);
    }

    [Fact]
    public async Task Following_reads_nothing_on_a_model_without_a_backlight()
    {
        _control.Access = _control.Access with { Capabilities = _control.Access.Capabilities with { KeyboardBacklight = false } };
        var controls = Controls(_control);

        await controls.FollowKeyboardBacklightAsync();

        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_follow_read_keeps_the_last_step_quietly()
    {
        await _controls.RefreshAsync(PortUse.None);
        _control.BacklightReadFails = true;

        await _controls.FollowKeyboardBacklightAsync();

        _controls.KeyboardBacklightLevel.Should().Be(1);
    }

    [Fact]
    public async Task The_backlight_never_changes_the_desired_state()
    {
        await _controls.RefreshAsync(PortUse.None);

        _controls.KeyboardBacklightLevel = 0;

        _service.Desired.Should().Be(AppSettings.Default.Desired);
    }
}
