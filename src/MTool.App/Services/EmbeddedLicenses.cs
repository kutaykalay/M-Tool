using System.IO;

namespace MTool.App.Services;

/// <summary>
/// The license texts built into the exe (see the csproj): the third-party notices first, as they are
/// short, then M-Tool's own GPL text. Line endings follow Windows, as the text goes into a text box.
/// </summary>
internal static class EmbeddedLicenses
{
    private static readonly string[] Resources = ["MTool.THIRD-PARTY-NOTICES.md", "MTool.LICENSE"];

    /// <summary>Null when a text is missing from the exe (a build without them).</summary>
    public static string? Read()
    {
        var texts = new List<string>();
        foreach (var name in Resources)
        {
            using var stream = typeof(EmbeddedLicenses).Assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            texts.Add(reader.ReadToEnd().ReplaceLineEndings(Environment.NewLine).Trim());
        }

        return string.Join(Environment.NewLine + Environment.NewLine, texts);
    }
}
