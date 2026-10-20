using System.Globalization;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Localization;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

/// <summary>The language list the settings window shows (Aşama 14): the same choices as the tray menu.</summary>
public sealed class LanguageViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeNotifier _notifier = new();
    private readonly StatusViewModel _status;

    public LanguageViewModelTests() => _status = new StatusViewModel(_notifier, new ImmediateDispatcher(), TimeProvider.System);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private LanguageViewModel ViewModel(string? language = null) => new(
        new ProfileService(new FakeP65Control(), ProfileCatalog.BuiltIn, new SettingsStore(_folder), AppSettings.Default with { Language = language }, new ListLog()),
        _status, _notifier, CultureInfo.GetCultureInfo("en-US"));

    [Fact]
    public void The_options_are_windows_first_then_every_language_by_its_own_name()
    {
        var vm = ViewModel();

        vm.Options.Select(o => o.Name).Should().Equal([null, .. SupportedLanguages.All]);
        vm.Options[0].Display.Should().Be(Strings.Lang_Windows);
        vm.Options.Skip(1).Select(o => o.Display).Should().Equal("English", "Türkçe", "中文（简体）", "Русский", "Español", "Português (Brasil)", "Deutsch", "Français");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("tr", "tr")]
    [InlineData("TR", "tr")]
    public void The_chosen_option_is_the_saved_language(string? saved, string? chosen)
    {
        ViewModel(saved).Chosen.Name.Should().Be(chosen);
    }

    [Fact]
    public async Task Saving_another_language_moves_the_mark()
    {
        var vm = ViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.ChangeCommand.ExecuteAsync("de");

        vm.Chosen.Name.Should().Be("de");
        changed.Should().Contain(nameof(LanguageViewModel.Chosen));
    }
}
