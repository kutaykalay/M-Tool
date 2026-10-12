using System.Globalization;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Tray;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Localization;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Tray;

/// <summary>The language submenu of the tray (Aşama 8, Adım 8): own names, the check, the saved message in the new language.</summary>
public sealed class TrayLanguageItemsTests : IDisposable
{
    private static readonly CultureInfo EnglishWindows = CultureInfo.GetCultureInfo("en-US");

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();
    private readonly StatusViewModel _status = new(new FakeNotifier(), new ImmediateDispatcher(), TimeProvider.System);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ProfileService Service(string? language = null, string? folder = null) =>
        new(_control, ProfileCatalog.BuiltIn, new SettingsStore(folder ?? _folder), AppSettings.Default with { Language = language }, new ListLog());

    private LanguageViewModel ViewModel(ProfileService service, CultureInfo? windowsUi = null) =>
        new(service, _status, windowsUi ?? EnglishWindows);

    private static ToolStripMenuItem[] Choices(ToolStripMenuItem menu) => [.. menu.DropDownItems.Cast<ToolStripMenuItem>()];

    private static async Task Click(LanguageViewModel vm, ToolStripMenuItem choice)
    {
        choice.PerformClick();
        await ((IAsyncRelayCommand)vm.ChangeCommand).ExecutionTask!;
    }

    [Fact]
    public void Lists_windows_first_then_every_language_by_its_own_name()
    {
        using var menu = TrayLanguageItems.For(ViewModel(Service()));

        Choices(menu).Select(i => i.Text).Should().Equal("Windows dili", "English", "Türkçe");
    }

    [Fact]
    public void Every_supported_language_has_a_choice_named_in_that_language()
    {
        using var menu = TrayLanguageItems.For(ViewModel(Service()));

        var names = Choices(menu).Skip(1).Select(i => i.Text).ToArray();

        names.Should().HaveCount(SupportedLanguages.All.Count);
        names.Should().OnlyContain(n => n != null && n.Length > 0 && char.IsUpper(n[0]));
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void The_names_do_not_depend_on_the_interface_language(string interfaceLanguage)
    {
        using var _ = TestCulture.Use(interfaceLanguage);
        using var menu = TrayLanguageItems.For(ViewModel(Service()));

        Choices(menu).Skip(1).Select(i => i.Text).Should().Equal("English", "Türkçe");
    }

    [Theory]
    [InlineData("tr", "Dil (Language)")]
    [InlineData("en", "Language")]
    public void The_title_says_Language_in_every_language(string interfaceLanguage, string expected)
    {
        using var _ = TestCulture.Use(interfaceLanguage);
        using var menu = TrayLanguageItems.For(ViewModel(Service()));

        menu.Text.Should().Be(expected);
        menu.Text.Should().Contain("Language");
    }

    [Theory]
    [InlineData(null, "Windows dili")]
    [InlineData("en", "English")]
    [InlineData("tr", "Türkçe")]
    public void Only_the_chosen_language_is_checked(string? chosen, string checkedText)
    {
        using var menu = TrayLanguageItems.For(ViewModel(Service(chosen)));

        Choices(menu).Where(i => i.Checked).Select(i => i.Text).Should().Equal(checkedText);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(2, "en")]
    [InlineData(3, "tr")]
    public async Task Choosing_calls_SetLanguage_with_that_value(int index, string? expected)
    {
        var service = Service(language: expected is null ? "tr" : null);
        var vm = ViewModel(service);
        using var menu = TrayLanguageItems.For(vm);

        await Click(vm, Choices(menu)[index - 1]);

        service.Language.Should().Be(expected);
        File.ReadAllText(Path.Combine(_folder, "settings.json")).Should().Contain(expected is null ? "\"language\": null" : $"\"{expected}\"");
    }

    [Fact]
    public async Task The_saved_message_is_in_the_new_language_not_the_current_one()
    {
        var vm = ViewModel(Service());
        using var menu = TrayLanguageItems.For(vm); // the test run is Turkish

        await Click(vm, Choices(menu).Single(i => i.Text == "English"));

        _status.Message.Should().StartWith("Language saved");
        _status.MessageKind.Should().Be(MessageKind.Info);
    }

    [Fact]
    public async Task Choosing_turkish_from_english_says_so_in_turkish()
    {
        using var _ = TestCulture.Use("en");
        var vm = ViewModel(Service());
        using var menu = TrayLanguageItems.For(vm);

        await Click(vm, Choices(menu).Single(i => i.Text == "Türkçe"));

        _status.Message.Should().StartWith("Dil kaydedildi");
    }

    [Fact]
    public async Task Choosing_windows_says_it_in_the_language_windows_resolves_to()
    {
        var vm = ViewModel(Service("tr"), CultureInfo.GetCultureInfo("en-GB"));
        using var menu = TrayLanguageItems.For(vm);

        await Click(vm, Choices(menu)[0]);

        _status.Message.Should().StartWith("Language saved");
    }

    [Fact]
    public async Task Choosing_windows_with_a_turkish_windows_says_it_in_turkish()
    {
        var vm = ViewModel(Service("en"), CultureInfo.GetCultureInfo("tr-TR"));
        using var menu = TrayLanguageItems.For(vm);

        await Click(vm, Choices(menu)[0]);

        _status.Message.Should().StartWith("Dil kaydedildi");
    }

    [Fact]
    public async Task A_failed_save_is_a_warning_and_not_the_saved_message()
    {
        var blocked = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocked, "");
        var vm = ViewModel(Service(folder: blocked));
        using var menu = TrayLanguageItems.For(vm);

        await Click(vm, Choices(menu).Single(i => i.Text == "English"));

        _status.MessageKind.Should().Be(MessageKind.Warning);
        _status.Message.Should().Contain("settings.json").And.NotContain("saved");
    }

    [Fact]
    public async Task The_menu_is_open_while_writing_is_locked_and_never_touches_the_ec()
    {
        _control.Access = _control.Access with { WriteMode = WriteMode.Locked };
        var vm = ViewModel(Service());
        using var menu = TrayLanguageItems.For(vm);

        menu.Enabled.Should().BeTrue();
        Choices(menu).Should().OnlyContain(i => i.Enabled);
        await Click(vm, Choices(menu).Single(i => i.Text == "English"));

        _control.Calls.Should().BeEmpty();
        _status.Message.Should().StartWith("Language saved");
    }
}
