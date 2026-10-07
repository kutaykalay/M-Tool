using System.Text.RegularExpressions;
using System.Xml.Linq;
using MTool.Tests.Fakes;

namespace MTool.Tests.Localization;

/// <summary>Text the user reads comes from the string resources; XAML may only carry the few words every language shares.</summary>
/// <remarks>
/// Covers attributes, setter values, element text and StringFormat. Text set from code (MessageBox,
/// window titles, view model constants) is not XAML; it moves in steps 4-6 with its own tests.
/// </remarks>
public sealed partial class XamlLiteralTests
{
    private static readonly HashSet<string> TextProperties =
        ["Text", "Content", "ToolTip", "Title", "Header", "Label", "AutomationProperties.Name", "AutomationProperties.HelpText"];

    private static readonly string[] MarkupExtensions = ["{Binding", "{x:Static", "{StaticResource", "{DynamicResource", "{TemplateBinding", "{x:Null"];

    /// <summary>Names and symbols that read the same in every language.</summary>
    private static readonly HashSet<string> Shared = ["CPU", "GPU", "M-Tool", "Cooler Boost", "–", "°C", "True", "False"];

    /// <summary>Today's Turkish text, per file. Step 4 moves it to the resources and empties this list.</summary>
    private static readonly Dictionary<string, HashSet<string>> NotMovedYet = new()
    {
        ["MainWindow.xaml"] =
        [
            "(fanlar tam hız)",
            "Açıkken fan profili ve performans modu prizde ve pilde ayrı hatırlanır. Kablo takılınca ya da çıkarılınca o kaynakta son seçtiğiniz ayar uygulanır. Şarj limiti ve Cooler Boost değişmez.",
            "COOLER BOOST", "Eğrileri düzenle", "FAN PROFİLİ", "Fan eğrilerini ve özel profilleri düzenle", "PERFORMANS",
            "Prizde ve pilde ayrı ayarları hatırla", "Uygula", "Yeniden uygula", "ŞARJ LİMİTİ", "Şu an:",
        ],
        ["FanCurveEditorWindow.xaml"] =
        [
            "%", "AD", "Eşik °C", "FAN", "Geri al", "Hazır profiller değiştirilemez; düzenlemek için kopyalayın.", "Hız %", "Kaydet",
            "Kaydet ve uygula", "Kopyala", "M-Tool · Fan eğrileri", "Nokta", "PROFİLLER", "Sil", "Uygula", "Yeniden adlandır",
            "hazır", "°C,", "İzin verilen",
        ],
        ["ConfirmDialog.xaml"] = ["Evet", "Hayır"],
    };

    [Fact]
    public void Xaml_text_is_bound_or_shared_by_every_language()
    {
        var unexpected = Literals().Where(literal => !IsAllowed(literal)).Select(literal => literal.ToString()).ToList();

        // One string, so a failure lists every literal rather than the first.
        string.Join(Environment.NewLine, unexpected).Should().BeEmpty();
    }

    [Fact]
    public void The_not_moved_list_holds_only_text_that_is_still_in_xaml()
    {
        var present = Literals().Select(literal => (literal.File, literal.Value)).ToHashSet();

        var stale = NotMovedYet.SelectMany(file => file.Value.Select(value => (file.Key, value))).Where(entry => !present.Contains(entry));

        stale.Should().BeEmpty("a moved text must leave the list too");
    }

    private static bool IsAllowed(Literal literal) =>
        Shared.Contains(literal.Value)
        || (NotMovedYet.TryGetValue(literal.File, out var file) && file.Contains(literal.Value))
        || literal.Value.All(IsIconGlyph);

    // Segoe MDL2 / Fluent icons live in the private use area.
    private static bool IsIconGlyph(char c) => c is >= (char)0xE000 and <= (char)0xF8FF;

    private static IEnumerable<Literal> Literals() =>
        RepoFiles.Under(Path.Combine("src", "MTool.App"), "*.xaml").SelectMany(file =>
        {
            var name = Path.GetFileName(file);
            return XDocument.Load(file).Descendants().SelectMany(element => LiteralsOf(name, element));
        });

    private static IEnumerable<Literal> LiteralsOf(string file, XElement element)
    {
        var tag = element.Name.LocalName;
        foreach (var attribute in element.Attributes().Where(a => TextProperties.Contains(a.Name.LocalName)))
        {
            if (TextOf(attribute.Value) is { } text)
            {
                yield return new Literal(file, tag, text);
            }
        }

        // <Setter Property="ToolTip" Value="..."/>
        if (tag == "Setter" && TextProperties.Contains((string?)element.Attribute("Property") ?? "")
            && TextOf((string?)element.Attribute("Value") ?? "") is { } setterText)
        {
            yield return new Literal(file, tag, setterText);
        }

        // <TextBlock>Kaydet</TextBlock>, <Run>…</Run>; numbers and colours in theme resources are not words.
        foreach (var node in element.Nodes().OfType<XText>().Where(t => t.Value.Any(char.IsLetter)))
        {
            yield return new Literal(file, tag, node.Value.Trim());
        }
    }

    /// <summary>The words a value shows by itself, or null when a binding or resource supplies them.</summary>
    private static string? TextOf(string value)
    {
        if (value.StartsWith("{}", StringComparison.Ordinal))
        {
            return value[2..];
        }

        if (!MarkupExtensions.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return value.Length == 0 ? null : value;
        }

        // A StringFormat with letters puts fixed words around the bound value.
        var format = StringFormat().Match(value);
        return format.Success && format.Groups[1].Value.Any(char.IsLetter) ? format.Groups[1].Value : null;
    }

    [GeneratedRegex(@"StringFormat\s*=\s*'([^']*)'")]
    private static partial Regex StringFormat();

    private sealed record Literal(string File, string Element, string Value)
    {
        public override string ToString() => $"{File} <{Element}> \"{Value}\"";
    }
}
