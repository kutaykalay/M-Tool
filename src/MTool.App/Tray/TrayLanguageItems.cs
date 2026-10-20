using System.Windows.Forms;
using MTool.App.Resources;
using MTool.App.ViewModels;
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
        foreach (var option in language.Options)
        {
            menu.DropDownItems.Add(Choice(option.Display, language, option.Name));
        }

        return menu;
    }

    private static ToolStripMenuItem Choice(string text, LanguageViewModel language, string? name) =>
        Item(text.Replace("&", "&&", StringComparison.Ordinal),
            isChecked: string.Equals(language.Current, name, StringComparison.OrdinalIgnoreCase),
            enabled: true,
            () => Run(language.ChangeCommand, name));
}
