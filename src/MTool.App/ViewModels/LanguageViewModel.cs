using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Resources;
using MTool.Core.Localization;
using MTool.Core.Profiles;

namespace MTool.App.ViewModels;

/// <summary>
/// The language choice of the tray menu. Only changes the settings file (never the EC), so it works
/// while writing is locked too. The language applies at the next start; the message that says so is
/// written in the language just chosen, so the user can read that it worked.
/// </summary>
public sealed partial class LanguageViewModel(ProfileService service, StatusViewModel status, CultureInfo windowsUi) : ObservableObject
{
    /// <summary>The chosen language, or null while M-Tool follows Windows.</summary>
    public string? Current => service.Language;

    /// <param name="language">A name from <see cref="SupportedLanguages"/>, or null for the Windows language.</param>
    [RelayCommand]
    private async Task ChangeAsync(string? language)
    {
        var result = await service.SetLanguageAsync(language);
        OnPropertyChanged(nameof(Current));
        if ((result.Error ?? result.SaveWarning) is { } problem)
        {
            status.ShowWarning(problem);
            return;
        }

        var shown = UiLanguage.Resolve(language, windowsUi, SupportedLanguages.All);
        status.ShowInfo(Strings.ResourceManager.GetString(nameof(Strings.Lang_Saved), shown) ?? Strings.Lang_Saved);
    }
}
