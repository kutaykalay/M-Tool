using System.Windows.Forms;
using MTool.App.ViewModels;
using MTool.Core.Profiles;

namespace MTool.App.Tray;

/// <summary>
/// The device part of the tray menu: profiles, then Cooler Boost and performance. Only what the
/// device record has; a locked control is there but disabled.
/// </summary>
internal static class TrayDeviceItems
{
    public static IReadOnlyList<ToolStripItem> For(ControlsViewModel controls)
    {
        var items = new List<ToolStripItem>();
        if (controls.ShowFanProfiles)
        {
            AddProfiles(items, controls);
        }

        var switches = new List<ToolStripItem>();
        if (controls.ShowCoolerBoost)
        {
            switches.Add(Item("Cooler Boost", controls.CoolerBoostOn == true, controls.CanWritePort,
                () => Run(controls.SetCoolerBoostCommand, controls.CoolerBoostOn != true)));
        }

        if (controls.ShowPerformance)
        {
            switches.Add(PerformanceMenu(controls));
        }

        if (items.Count > 0 && switches.Count > 0)
        {
            items.Add(new ToolStripSeparator());
        }

        items.AddRange(switches);
        return items.AsReadOnly();
    }

    /// <summary>The menu was built when it opened; the state may have changed since (a write started, writes locked).</summary>
    public static void Run(System.Windows.Input.ICommand command, object? parameter)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    public static ToolStripMenuItem Item(string text, bool isChecked, bool enabled, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { Checked = isChecked, Enabled = enabled };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static void AddProfiles(List<ToolStripItem> items, ControlsViewModel controls)
    {
        var firstCustom = controls.ProfileNames.FirstOrDefault(n => !ProfileCatalog.IsBuiltIn(n));
        foreach (var name in controls.ProfileNames)
        {
            if (name == firstCustom)
            {
                items.Add(new ToolStripSeparator()); // the user's own profiles after the built-in ones
            }

            // "&" marks a mnemonic in a menu; a profile named "A&B" must show as written.
            items.Add(Item(name.Replace("&", "&&", StringComparison.Ordinal), name == controls.ActiveProfile, controls.CanWrite,
                () => Run(controls.SelectProfileCommand, name)));
        }
    }

    private static ToolStripMenuItem PerformanceMenu(ControlsViewModel controls)
    {
        var menu = new ToolStripMenuItem($"Performans: {controls.PerformanceLabel}");
        foreach (var option in controls.PerformanceOptions)
        {
            menu.DropDownItems.Add(Item(option.Name, controls.ActivePerformance == option.Mode, controls.CanWrite,
                () => Run(controls.SetPerformanceCommand, option.Mode)));
        }

        return menu;
    }
}
