using System.Text;

namespace MTool.App.Reports;

/// <summary>
/// The address of the prefilled "Device report" issue form. Host, path and field names are constants;
/// only the values are variable, and each is cleaned, cut and percent-encoded. A value that is not
/// known is left out of the address. GitHub cannot take a file through the address: the user drags
/// the zip into the form.
/// </summary>
internal static class IssueUrl
{
    public const string Host = "github.com";
    public const string FormPath = "/kutaykalay/M-Tool/issues/new";
    public const string Template = "device-report.yml";
    public const string TitlePrefix = "[Device report] ";

    /// <summary>Well below what browsers and GitHub accept; the form needs only a model and a firmware.</summary>
    public const int MaxUrlLength = 1500;

    private const int MaxModelEncodedLength = 400;
    private const int MaxFirmwareEncodedLength = 150;

    /// <param name="model">The laptop model from SMBIOS; null or blank when unknown.</param>
    /// <param name="firmware">The EC firmware version; null or blank when unknown.</param>
    public static Uri Build(string? model, string? firmware)
    {
        var fields = new List<string> { $"template={Uri.EscapeDataString(Template)}" };
        if (Fit(model, MaxModelEncodedLength) is { } cleanModel)
        {
            fields.Add($"title={Uri.EscapeDataString(TitlePrefix + cleanModel)}");
            fields.Add($"model={Uri.EscapeDataString(cleanModel)}");
        }

        if (Fit(firmware, MaxFirmwareEncodedLength) is { } cleanFirmware)
        {
            fields.Add($"firmware={Uri.EscapeDataString(cleanFirmware)}");
        }

        return new UriBuilder(Uri.UriSchemeHttps, Host) { Path = FormPath, Query = string.Join('&', fields) }.Uri;
    }

    /// <summary>Single line, no control characters, cut so its encoded form fits; null when nothing is left.</summary>
    private static string? Fit(string? value, int maxEncodedLength)
    {
        var text = new StringBuilder();
        var lastWasSpace = true; // also trims the start
        foreach (var rune in (value ?? "").EnumerateRunes())
        {
            if (rune == Rune.ReplacementChar || (Rune.IsControl(rune) && !Rune.IsWhiteSpace(rune)))
            {
                continue; // a broken surrogate or a control character
            }

            var isSpace = Rune.IsWhiteSpace(rune);
            if (isSpace && lastWasSpace)
            {
                continue;
            }

            var next = isSpace ? " " : rune.ToString();
            if (Uri.EscapeDataString(text.ToString() + next).Length > maxEncodedLength)
            {
                break;
            }

            text.Append(next);
            lastWasSpace = isSpace;
        }

        var clean = text.ToString().Trim();
        return clean.Length == 0 ? null : clean;
    }
}
