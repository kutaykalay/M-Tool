using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Resources;
using MTool.Core.Localization;
using MTool.Core.Profiles;

namespace MTool.App.ViewModels;

/// <summary>A language to choose: its <paramref name="Name"/> from <see cref="SupportedLanguages"/>, or null for the Windows language.</summary>
public sealed record LanguageOption(string? Name, string Display);

/// <summary>
/// The language choice of the tray menu and the settings window. Only changes the settings file (never the EC), so it works
/// while writing is locked too. The language applies at the next start; the message that says so is
/// written in the language just chosen, so the user can read that it worked.
/// </summary>
public sealed partial class LanguageViewModel(ProfileService service, StatusViewModel status, INotifier notifier, CultureInfo windowsUi) : ObservableObject
{
    private const string AppName = "M-Tool";

    /// <summary>The chosen language, or null while M-Tool follows Windows.</summary>
    public string? Current => service.Language;

    /// <summary>"Windows language", then every supported language by its own name (never translated).</summary>
    public IReadOnlyList<LanguageOption> Options { get; } =
        [new(null, Strings.Lang_Windows), .. SupportedLanguages.All.Select(name => new LanguageOption(name, OwnName(name)))];

    /// <summary>The option of <see cref="Current"/>: the settings window marks it.</summary>
    public LanguageOption Chosen =>
        Options.FirstOrDefault(o => string.Equals(o.Name, Current, StringComparison.OrdinalIgnoreCase)) ?? Options[0];

    /// <param name="language">A name from <see cref="SupportedLanguages"/>, or null for the Windows language.</param>
    [RelayCommand]
    private async Task ChangeAsync(string? language)
    {
        var result = await service.SetLanguageAsync(language);
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Chosen));
        if ((result.Error ?? result.SaveWarning) is { } problem)
        {
            Announce(problem, isWarning: true);
            return;
        }

        var shown = UiLanguage.Resolve(language, windowsUi, SupportedLanguages.All);
        Announce(Strings.ResourceManager.GetString(nameof(Strings.Lang_Saved), shown) ?? Strings.Lang_Saved, isWarning: false);
    }

    /// <summary>"Deutsch", "English"; some languages write their name in lower case ("русский"), so the first letter is raised.</summary>
    private static string OwnName(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        var native = culture.NativeName;
        return string.Concat(culture.TextInfo.ToUpper(native[0]).ToString(), native.AsSpan(1));
    }

    /// <summary>Writes the message to the status band and also to a tray balloon: the window may be closed.</summary>
    private void Announce(string message, bool isWarning)
    {
        if (isWarning)
        {
            status.ShowWarning(message);
        }
        else
        {
            status.ShowInfo(message);
        }

        notifier.ShowInfo(AppName, message);
    }
}
