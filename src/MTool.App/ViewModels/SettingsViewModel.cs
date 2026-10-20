namespace MTool.App.ViewModels;

/// <summary>
/// The settings window's data: the same view models the tray and the main window use, so the three
/// places never disagree, plus the About tab and its update check.
/// </summary>
internal sealed class SettingsViewModel(
    SignInStartViewModel signInStart, LanguageViewModel language, PowerSwitchViewModel powerSwitch, StatusViewModel status, AboutViewModel about,
    UpdatesViewModel updates)
{
    public SignInStartViewModel SignInStart { get; } = signInStart;

    public LanguageViewModel Language { get; } = language;

    public PowerSwitchViewModel PowerSwitch { get; } = powerSwitch;

    public StatusViewModel Status { get; } = status;

    public AboutViewModel About { get; } = about;

    public UpdatesViewModel Updates { get; } = updates;
}
