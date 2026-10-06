namespace MTool.Core.Device.Config;

/// <summary>Config text that reaches logs and messages: control characters could forge a log line.</summary>
internal static class ConfigText
{
    private const int MaxShownLength = 64;

    public static bool HasControl(string text) => text.Any(char.IsControl);

    /// <summary>Control characters as '?', cut to a readable length.</summary>
    public static string Show(string? text)
    {
        if (text is null)
        {
            return "null";
        }

        var safe = new string([.. text.Select(c => char.IsControl(c) ? '?' : c)]);
        return safe.Length <= MaxShownLength ? safe : safe[..MaxShownLength] + "…";
    }
}
