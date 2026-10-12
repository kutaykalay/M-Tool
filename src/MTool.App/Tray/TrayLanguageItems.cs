using System.Globalization;
using System.Windows.Forms;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Localization;
using static MTool.App.Tray.TrayDeviceItems;

namespace MTool.App.Tray;

/// <summary>
/// The language submenu: "Windows language", then every supported language by its own name (never
/// translated). It only changes the settings file, so it is enabled while writing is locked.
/// </summary>
internal static class TrayLanguageItems
{
    public static ToolStripMenuItem For(LanguageViewModel language)
    {
        var menu = new ToolStripMenuItem(Strings.Lang_Menu);
        menu.DropDownItems.Add(Choice(Strings.Lang_Windows, language, null));
        foreach (var name in SupportedLanguages.All)
        {
            menu.DropDownItems.Add(Choice(OwnName(name), language, name));
        }

        return menu;
    }

    private static ToolStripMenuItem Choice(string text, LanguageViewModel language, string? name) =>
        Item(text.Replace("&", "&&", StringComparison.Ordinal),
            isChecked: string.Equals(language.Current, name, StringComparison.OrdinalIgnoreCase),
            enabled: true,
            () => Run(language.ChangeCommand, name));

    /// <summary>"Deutsch", "English"; some languages write their name in lower case ("русский"), so the first letter is raised.</summary>
    private static string OwnName(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        var native = culture.NativeName;
        return string.Concat(culture.TextInfo.ToUpper(native[0]).ToString(), native.AsSpan(1));
    }
}
