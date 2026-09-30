using System.Windows;
using Microsoft.Win32;

namespace MTool.App.Theme;

/// <summary>
/// Follows Windows' app theme (Settings → Personalization → Colors): swaps Light.xaml / Dark.xaml
/// in the application resources, now and whenever the setting changes.
/// </summary>
internal sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";

    private static readonly Uri LightUri = new("pack://application:,,,/Theme/Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/Theme/Dark.xaml");

    private readonly Application _app;

    public ThemeManager(Application app)
    {
        _app = app;
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Raised on the UI thread after the theme changed.</summary>
    public event Action? Changed;

    public bool IsDark { get; private set; }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            _app.Dispatcher.BeginInvoke(() =>
            {
                if (ReadIsDark() != IsDark)
                {
                    Apply();
                    Changed?.Invoke();
                }
            });
        }
    }

    /// <summary>App.xaml keeps the theme as the first merged dictionary.</summary>
    private void Apply()
    {
        IsDark = ReadIsDark();
        _app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = IsDark ? DarkUri : LightUri };
    }

    /// <summary>Missing key (older Windows) means light.</summary>
    private static bool ReadIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(AppsUseLightTheme) is 0;
    }
}
