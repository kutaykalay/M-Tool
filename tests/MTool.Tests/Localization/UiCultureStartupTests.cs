using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using MTool.App.Startup;
using MTool.Tests.Fakes;

namespace MTool.Tests.Localization;

/// <summary>
/// A UI culture set inside an async method is undone when that method first awaits. If the start-up
/// set the language there, windows opened later from a click (the curve editor) came up in the Windows
/// language. The language is set in synchronous start-up code; these tests keep it there.
/// </summary>
public sealed class UiCultureStartupTests
{
    [Fact]
    public async Task A_culture_set_inside_an_async_method_does_not_outlive_it()
    {
        using var _ = TestCulture.Use("tr-TR");

        await SetInsideAsync("en");

        CultureInfo.CurrentUICulture.Name.Should().Be("tr-TR", "this is why the start-up must not set the language in an async method");
    }

    [Fact]
    public void App_OnStartup_is_not_async()
    {
        var onStartup = typeof(MTool.App.App).GetMethod("OnStartup", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(StartupEventArgs)]);

        onStartup.Should().NotBeNull();
        onStartup!.GetCustomAttribute<AsyncStateMachineAttribute>().Should().BeNull();
    }

    [Fact]
    public void Settings_loading_that_sets_the_language_is_not_async()
    {
        var load = typeof(GuiBootstrapper).GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(m => m.Name == nameof(GuiBootstrapper.LoadSettings));

        load.ReturnType.Should().Be<GuiBootstrapper.LoadedSettings>();
        load.GetCustomAttribute<AsyncStateMachineAttribute>().Should().BeNull();
    }

    private static async Task SetInsideAsync(string name)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        await Task.Yield();
    }
}
