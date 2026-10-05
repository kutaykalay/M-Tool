using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.Core.Device;
using MTool.Core.Profiles;

namespace MTool.App.ViewModels;

/// <summary>
/// The fan curve editor: a list of profiles and a draft of the selected one. Moving a point only
/// changes the draft; saving writes settings.json, never the EC. Only a saved profile is applied,
/// through <see cref="ControlsViewModel.SelectProfileCommand"/> like a click in the main window.
/// One per editor window: disposed when the window closes, so it stops following the service.
/// </summary>
public sealed partial class FanCurveEditorViewModel : ObservableObject, IDisposable
{
    private const string DiscardQuestion = "Kaydedilmemiş değişiklikler atılsın mı?";

    private readonly ProfileService _service;
    private readonly ControlsViewModel _controls;
    private readonly StatusViewModel _status;
    private readonly IConfirm _confirm;
    private readonly IUiDispatcher _ui;
    private readonly Action _onCatalogChanged;
    private readonly System.ComponentModel.PropertyChangedEventHandler _onControlsChanged;
    private ProfileItem _selectedProfile;
    private FanCurves _saved;

    public FanCurveEditorViewModel(
        ProfileService service, ControlsViewModel controls, StatusViewModel status, IConfirm confirm, IUiDispatcher ui)
    {
        (_service, _controls, _status, _confirm, _ui) = (service, controls, status, confirm, ui);
        var (desired, catalog) = service.DesiredWithCatalog;
        var start = catalog.Find(desired.FanProfile) ?? catalog.Profiles[0];
        Profiles = ItemsOf(catalog);
        _selectedProfile = ItemOf(start);
        _saved = start.Curves;
        Draft = start.Curves;
        NewName = start.Name;

        _onCatalogChanged = () => ui.Post(OnCatalogChanged);
        _onControlsChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(ControlsViewModel.CanWrite))
            {
                NotifyCommands();
            }
        };
        service.CatalogChanged += _onCatalogChanged;
        controls.PropertyChanged += _onControlsChanged;
    }

    /// <summary>The built-in profiles first, then the custom ones.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ProfileItem> Profiles { get; private set; }

    /// <summary>Leaving a dirty draft asks first; "no" (or a running command) keeps the current profile.</summary>
    public ProfileItem? SelectedProfile
    {
        get => _selectedProfile;
        set => TrySelect(value);
    }

    [ObservableProperty]
    public partial FanCurves Draft { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Points))]
    public partial CurveFan SelectedFan { get; set; }

    /// <summary>The name box: the selected profile's name until the user types a new one for renaming.</summary>
    [ObservableProperty]
    public partial string NewName { get; set; }

    /// <summary>The result of the last command, shown in the editor.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    public bool IsBuiltIn => _selectedProfile.IsBuiltIn;

    public bool IsDirty => Draft != _saved;

    /// <summary>What the gateway would refuse, e.g. the envelope rule that spans several points.</summary>
    public IReadOnlyList<string> Errors => CurveValidator.ValidateWithFactoryOffsets(Draft);

    /// <summary>For the view: the list's runtime type may not have a bindable Count.</summary>
    public bool HasErrors => Errors.Count > 0;

    public IReadOnlyList<PointViewModel> Points
    {
        get
        {
            var curve = CurveOf(Draft, SelectedFan);
            var offsets = OffsetsOf(SelectedFan);
            return [.. curve.Points.Select((p, i) =>
                new PointViewModel(i, p.UpThresholdC, p.SpeedPercent, CurveEditing.Limits(curve, i, offsets)))];
        }
    }

    /// <summary>Dragging and the number table both come here; the point lands on the nearest allowed place.</summary>
    public void MovePoint(CurveFan fan, int index, int upC, int speedPercent)
    {
        if (IsBuiltIn || IsBusy)
        {
            return;
        }

        var moved = CurveEditing.Move(CurveOf(Draft, fan), index, upC, speedPercent, OffsetsOf(fan));
        var draft = fan == CurveFan.Cpu ? Draft with { Cpu = moved } : Draft with { Gpu = moved };
        if (draft == Draft)
        {
            // Clamped back to where it was: the table must still drop the typed value and show the point.
            OnPropertyChanged(nameof(Points));
            return;
        }

        Draft = draft;
    }

    /// <summary>An event already posted may still run; it only updates this unused view model.</summary>
    public void Dispose()
    {
        _service.CatalogChanged -= _onCatalogChanged;
        _controls.PropertyChanged -= _onControlsChanged;
    }

    /// <summary>Before the window closes; the draft is lost when the app exits from the tray.</summary>
    public bool ConfirmClose() => !IsDirty || _confirm.Ask(DiscardQuestion);

    private bool CanEditSelected() => !IsBuiltIn && !IsBusy;

    private bool CanSave() => CanEditSelected() && IsDirty && Errors.Count == 0;

    private bool CanSaveAndApply() => CanSave() && _controls.CanWrite;

    private bool CanApply() => !IsDirty && !IsBusy && _controls.CanWrite;

    // A change of letter case alone is a rename.
    private bool CanRename() =>
        CanEditSelected() && !string.IsNullOrWhiteSpace(NewName) && ProfileNameRules.Normalize(NewName) != _selectedProfile.Name;

    private bool CanCopy() => !IsBusy;

    private bool CanRevert() => IsDirty && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (await SaveDraftAsync())
        {
            Message ??= "Kaydedildi. EC'ye yazmak için Uygula.";
        }
    });

    [RelayCommand(CanExecute = nameof(CanSaveAndApply))]
    private Task SaveAndApplyAsync() => RunAsync(async () =>
    {
        if (await SaveDraftAsync())
        {
            await ApplySelectedAsync();
        }
    });

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyAsync() => RunAsync(ApplySelectedAsync);

    [RelayCommand(CanExecute = nameof(CanRevert))]
    private void Revert() => Draft = _saved;

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private Task CopyAsync() => RunAsync(async () =>
    {
        if (IsDirty && !_confirm.Ask(DiscardQuestion))
        {
            return;
        }

        var name = ProfileNameRules.NextCopyName(_selectedProfile.Name, _service.Catalog);
        if (Succeeded(await _service.AddProfileAsync(name, _saved)))
        {
            ShowSaved(name, keepDraft: false);
        }
    });

    [RelayCommand(CanExecute = nameof(CanRename))]
    private Task RenameAsync() => RunAsync(async () =>
    {
        var newName = NewName;
        if (Succeeded(await _service.RenameProfileAsync(_selectedProfile.Name, newName)))
        {
            ShowSaved(ProfileNameRules.Normalize(newName), keepDraft: IsDirty);
        }
    });

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        var name = _selectedProfile.Name;
        if (!_confirm.Ask($"\"{name}\" profili silinsin mi?"))
        {
            return;
        }

        if (Succeeded(await _service.DeleteProfileAsync(name)))
        {
            ShowSaved(_service.Desired.FanProfile, keepDraft: false);
        }
    });

    private async Task<bool> SaveDraftAsync()
    {
        var name = _selectedProfile.Name;
        if (!Succeeded(await _service.SaveCurvesAsync(name, Draft)))
        {
            return false;
        }

        ShowSaved(name, keepDraft: false);
        return true;
    }

    /// <summary>Keeps an earlier message of the same command (a save warning) in front of the result.</summary>
    private async Task ApplySelectedAsync()
    {
        // Checked again: the tray may have started a command while this one was saving.
        if (!_controls.CanWrite)
        {
            Message = Join(Message, "Başka bir komut sürüyor ya da yazma kapalı; profil uygulanmadı, tekrar deneyin.");
            return;
        }

        await _controls.SelectProfileCommand.ExecuteAsync(_selectedProfile.Name);
        Message = Join(Message, _status.Message);
    }

    private static string? Join(string? first, string? second) =>
        first is null ? second : second is null ? first : $"{first} {second}";

    /// <summary>Shows why a change was refused, or a save warning; true when the change was made.</summary>
    private bool Succeeded(ProfileCommandResult result)
    {
        Message = result.Error ?? result.SaveWarning;
        return result.Error is null;
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        Message = null;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void TrySelect(ProfileItem? item)
    {
        // Null comes from the list while its items are replaced; the selection is restored after.
        if (item is null || item == _selectedProfile || _service.Catalog.Find(item.Name) is not { } profile)
        {
            return;
        }

        if (IsBusy)
        {
            Message = "Bir komut sürüyor; bitince profil seçilebilir.";
        }

        if (IsBusy || (IsDirty && !_confirm.Ask(DiscardQuestion)))
        {
            // Raised later: a list ignores a change notification that arrives inside its own set.
            _ui.Post(() => OnPropertyChanged(nameof(SelectedProfile)));
            return;
        }

        Message = null;
        Show(profile, keepDraft: false);
    }

    /// <summary>The profiles changed (maybe by this editor, maybe elsewhere); a dirty draft is kept.</summary>
    private void OnCatalogChanged()
    {
        Profiles = ItemsOf(_service.Catalog);

        // A rename or delete in flight: its command shows the right profile when it is done.
        ShowSaved(_selectedProfile.Name, keepDraft: IsDirty);
    }

    private void ShowSaved(string name, bool keepDraft)
    {
        if (_service.Catalog.Find(name) is { } profile)
        {
            Show(profile, keepDraft);
        }
    }

    private void Show(FanProfile profile, bool keepDraft)
    {
        _selectedProfile = ItemOf(profile);
        _saved = profile.Curves;
        NewName = profile.Name;
        if (!keepDraft)
        {
            Draft = profile.Curves;
        }

        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(IsBuiltIn));
        OnDraftChanged(Draft);
    }

    partial void OnDraftChanged(FanCurves value)
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Errors));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(Points));
        NotifyCommands();
    }

    partial void OnNewNameChanged(string value) => RenameCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    private void NotifyCommands()
    {
        foreach (var command in new IRelayCommand[]
                 { SaveCommand, SaveAndApplyCommand, ApplyCommand, RevertCommand, CopyCommand, RenameCommand, DeleteCommand })
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private static ProfileItem ItemOf(FanProfile profile) => new(profile.Name, ProfileCatalog.IsBuiltIn(profile.Name));

    private static ProfileItem[] ItemsOf(ProfileCatalog catalog) => [.. catalog.Profiles.Select(ItemOf)];

    private static FanCurve CurveOf(FanCurves curves, CurveFan fan) => fan == CurveFan.Cpu ? curves.Cpu : curves.Gpu;

    private static IReadOnlyList<int> OffsetsOf(CurveFan fan) =>
        fan == CurveFan.Cpu ? FactoryDefaults.CpuDownOffsets : FactoryDefaults.GpuDownOffsets;
}
