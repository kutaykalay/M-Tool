using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Resources;
using MTool.Core.Power;
using MTool.Core.Profiles;

namespace MTool.App.ViewModels;

/// <summary>
/// "Separate settings on AC and battery": the switch and the "now on AC/battery" line. Switching
/// only changes the settings file, never the EC, so it works while writing is locked too. The
/// source line shows what Windows reports right now; the switch itself acts a few seconds later
/// (<see cref="PowerSourceSwitcher"/>).
/// </summary>
public sealed partial class PowerSwitchViewModel : ObservableObject
{
    private readonly ProfileService _service;
    private readonly IPowerSource _source;
    private readonly StatusViewModel _status;

    public PowerSwitchViewModel(ProfileService service, IPowerSource source, StatusViewModel status, IUiDispatcher ui)
    {
        (_service, _source, _status) = (service, source, status);
        IsOn = service.PowerSwitch.Enabled;
        SourceLabel = LabelOf(source.Current);
        source.Changed += () => ui.Post(() => SourceLabel = LabelOf(_source.Current));
    }

    [ObservableProperty]
    public partial bool IsOn { get; private set; }

    /// <summary>Null (the line is hidden) when Windows cannot tell.</summary>
    [ObservableProperty]
    public partial string? SourceLabel { get; private set; }

    private static string? LabelOf(PowerSource? source) => source switch
    {
        PowerSource.Ac => Strings.PowerSwitch_OnAc,
        PowerSource.Battery => Strings.PowerSwitch_OnBattery,
        _ => null,
    };

    /// <summary>A failed save still changes the running state; the warning says it is lost on restart.</summary>
    [RelayCommand]
    private async Task ToggleAsync()
    {
        var result = await _service.SetPowerSwitchAsync(!IsOn);
        IsOn = _service.PowerSwitch.Enabled;
        if ((result.Error ?? result.SaveWarning) is { } problem)
        {
            _status.ShowWarning(problem);
        }
    }
}
