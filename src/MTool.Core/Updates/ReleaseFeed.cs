using System.Text.Json;

namespace MTool.Core.Updates;

/// <summary>
/// Reads GitHub's list of releases (<c>GET /repos/{owner}/{repo}/releases</c>). Only <c>tag_name</c>,
/// <c>draft</c> and <c>prerelease</c> are read: the names, notes and addresses in the answer are never
/// kept, so nothing the server says reaches the window or the log except a checked version.
/// </summary>
public static class ReleaseFeed
{
    /// <summary>Root array, release object, assets array, asset object, uploader object, and some room.</summary>
    private const int MaxDepth = 8;

    /// <summary>The highest release that is not a draft; null when the answer has none or is not a release list.</summary>
    /// <param name="includePrerelease">Also take releases whose tag or flag says pre-release.</param>
    public static SemVersion? Latest(string body, bool includePrerelease)
    {
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = MaxDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return document.RootElement.EnumerateArray()
                .Select(release => Version(release, includePrerelease))
                .OfType<SemVersion>()
                .Max();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SemVersion? Version(JsonElement release, bool includePrerelease)
    {
        if (release.ValueKind != JsonValueKind.Object
            || !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
            || !IsFlag(release, "draft", out var draft) || draft
            || !IsFlag(release, "prerelease", out var prerelease)
            || !SemVersion.TryParseTag(tag.GetString(), out var version))
        {
            return null;
        }

        return includePrerelease || !(prerelease || version.IsPrerelease) ? version : null;
    }

    private static bool IsFlag(JsonElement release, string name, out bool value)
    {
        value = false;
        if (!release.TryGetProperty(name, out var flag) || flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = flag.GetBoolean();
        return true;
    }
}
