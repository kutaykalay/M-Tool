using System.Text.RegularExpressions;
using System.Windows.Markup;
using MTool.App.Resources;
using MTool.Tests.Fakes;

namespace MTool.Tests.Localization;

/// <summary>
/// XAML resolves <c>{x:Static res:Strings.Key}</c> when the window opens, by reflection, and sees public members only.
/// Nothing else in the tests loads a window, so a key that is missing or not public would only show when the app starts.
/// This asks WPF's own <see cref="StaticExtension"/> for every key a window uses.
/// </summary>
public sealed partial class XamlStaticReferenceTests
{
    [Fact]
    public void Every_string_a_window_reads_resolves_the_way_WPF_resolves_it()
    {
        var keys = ReferencedKeys().ToList();
        keys.Should().NotBeEmpty();
        var failures = new List<string>();

        foreach (var key in keys)
        {
            try
            {
                var value = new StaticExtension($"res:Strings.{key}").ProvideValue(new Services());
                value.Should().BeOfType<string>();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                failures.Add($"{key}: {ex.Message}");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    private static IEnumerable<string> ReferencedKeys() =>
        RepoFiles.Under(Path.Combine("src", "MTool.App"), "*.xaml")
            .SelectMany(file => StaticKey().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .Distinct();

    [GeneratedRegex(@"\{x:Static\s+res:Strings\.(\w+)\}")]
    private static partial Regex StaticKey();

    /// <summary>What the XAML loader hands the extension: the prefix <c>res</c> stands for <see cref="Strings"/>' namespace.</summary>
    private sealed class Services : IServiceProvider, IXamlTypeResolver
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IXamlTypeResolver) ? this : null;

        public Type Resolve(string qualifiedTypeName) =>
            qualifiedTypeName == "res:Strings" ? typeof(Strings) : throw new InvalidOperationException(qualifiedTypeName);
    }
}
