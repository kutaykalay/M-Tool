using System.Globalization;
using CommunityToolkit.Mvvm.Input;
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
    public async Task Choosing_another_option_saves_it_like_the_tray_does()
    {
        var vm = ViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Chosen = vm.Options.Single(o => o.Name == "de");
        await ((IAsyncRelayCommand)vm.ChangeCommand).ExecutionTask!;

        vm.Current.Should().Be("de");
        vm.Chosen.Name.Should().Be("de");
        changed.Should().Contain(nameof(LanguageViewModel.Chosen));
        _notifier.Infos.Should().ContainSingle();
    }

    [Fact]
    public void Choosing_the_option_already_chosen_saves_nothing()
    {
        var vm = ViewModel("fr");

        vm.Chosen = vm.Options.Single(o => o.Name == "fr");

        ((IAsyncRelayCommand)vm.ChangeCommand).ExecutionTask.Should().BeNull();
        _notifier.Infos.Should().BeEmpty();
    }

    [Fact]
    public void A_null_choice_is_ignored()
    {
        var vm = ViewModel("fr");

        vm.Chosen = null!;

        vm.Chosen.Name.Should().Be("fr");
        _notifier.Infos.Should().BeEmpty();
    }
}
