using CommunityToolkit.Mvvm.ComponentModel;
using MTool.App.Resources;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.App.ViewModels;

/// <summary>One fan mode button: the mode it writes and its label.</summary>
internal sealed record Wmi2FanOption(Wmi2FanMode Mode, string Name);

/// <summary>What the WMI2 window shows of experimental control: which buttons, the band, the choices and the charge limit draft.</summary>
internal sealed partial class Wmi2ControlsViewModel
{
    private byte? _draftFrom; // the charge byte the draft last followed; a round with the same byte leaves the user's draft alone

    public IReadOnlyList<PerformanceOption> PerformanceOptions { get; } = Array.AsReadOnly(
        [.. Wmi2WriteCeiling.StandardPerformance.Keys.Order().Select(m => new PerformanceOption(m, Texts.Mode(m)))]);

    /// <summary>Only the fan modes the ceiling writes: a custom curve is MSI Center's.</summary>
    public IReadOnlyList<Wmi2FanOption> FanModeOptions { get; } = Array.AsReadOnly(
        [.. Wmi2WriteCeiling.FanModeValues.Keys.Order().Select(m => new Wmi2FanOption(m, Wmi2LiveViewModel.FanModeName(m)!))]);

    public int ChargeLimitMin => Wmi2WriteCeiling.MinChargeLimitPercent;

    public int ChargeLimitMax => Wmi2WriteCeiling.MaxChargeLimitPercent;

    /// <summary>The limit "Apply" writes; follows the current limit when that changes, within what M-Tool sets.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChargeLimitDraftLabel))]
    public partial int ChargeLimitDraft { get; set; } = Wmi2WriteCeiling.MaxChargeLimitPercent;

    public string ChargeLimitDraftLabel => string.Format(Strings.Format_Percent, ChargeLimitDraft);

    public bool IsOpen => State == Wmi2ControlState.Open;

    public bool ShowEnable => State is Wmi2ControlState.Offered or Wmi2ControlState.Unavailable;

    public bool ShowTurnOff => State is Wmi2ControlState.Open or Wmi2ControlState.Unavailable;

    /// <summary>The warning band over the live values: what control writes while it is open, what reading is otherwise.</summary>
    public string Band => IsOpen ? Strings.Wmi2_ControlBand : Strings.Wmi2_ExperimentalBand;

    private void FollowChargeLimit(Wmi2Status? status)
    {
        if (status is null || status.ChargeRaw == _draftFrom)
        {
            return;
        }

        _draftFrom = status.ChargeRaw;
        ChargeLimitDraft = Math.Clamp(status.ChargeLimitPercent ?? ChargeLimitMax, ChargeLimitMin, ChargeLimitMax);
    }
}
