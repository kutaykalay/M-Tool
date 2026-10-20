using System.Diagnostics.CodeAnalysis;

namespace MTool.Core.Updates;

/// <summary>
/// A SemVer 2.0.0 version (semver.org), compared by its precedence; build metadata is dropped. Only
/// ASCII is taken (no Unicode digits), numbers have at most nine digits and the whole text at most
/// <see cref="MaxLength"/> characters: the text may come from the network.
/// </summary>
public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    public const int MaxLength = 64;

    private const int MaxNumberDigits = 9;

    private readonly string[] _prerelease;

    private SemVersion(int major, int minor, int patch, string[] prerelease)
    {
        (Major, Minor, Patch, _prerelease) = (major, minor, patch, prerelease);
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public bool IsPrerelease => _prerelease.Length > 0;

    /// <exception cref="FormatException">Not a version this class takes.</exception>
    public static SemVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException("Not a SemVer 2.0.0 version.");

    public static bool TryParse(string? text, [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        if (text is null || text.Length > MaxLength)
        {
            return false;
        }

        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0 && !AreIdentifiers(text[(plus + 1)..], numeric: false))
        {
            return false;
        }

        var core = plus >= 0 ? text[..plus] : text;
        var dash = core.IndexOf('-', StringComparison.Ordinal);
        var numbers = (dash >= 0 ? core[..dash] : core).Split('.');
        string[] prerelease = dash >= 0 ? core[(dash + 1)..].Split('.') : [];
        if (numbers.Length != 3 || (dash >= 0 && !AreIdentifiers(core[(dash + 1)..], numeric: true)))
        {
            return false;
        }

        if (!TryNumber(numbers[0], out var major) || !TryNumber(numbers[1], out var minor) || !TryNumber(numbers[2], out var patch))
        {
            return false;
        }

        version = new SemVersion(major, minor, patch, prerelease);
        return true;
    }

    /// <summary>A release tag: a lower-case <c>v</c>, then a version without build metadata (<c>v0.16.0-beta.1</c>).</summary>
    public static bool TryParseTag(string? tag, [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        return tag is ['v', .. var rest] && !rest.Contains('+', StringComparison.Ordinal) && TryParse(rest, out version);
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
        {
            return core;
        }

        // A version without a pre-release is higher than any with one.
        if (IsPrerelease != other.IsPrerelease)
        {
            return IsPrerelease ? -1 : 1;
        }

        for (var i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
        {
            var order = CompareIdentifiers(_prerelease[i], other._prerelease[i]);
            if (order != 0)
            {
                return order;
            }
        }

        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    public bool Equals(SemVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => Equals(obj as SemVersion);

    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    public override string ToString() =>
        IsPrerelease ? $"{Major}.{Minor}.{Patch}-{string.Join('.', _prerelease)}" : $"{Major}.{Minor}.{Patch}";

    public static bool operator <(SemVersion left, SemVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemVersion left, SemVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemVersion left, SemVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemVersion left, SemVersion right) => left.CompareTo(right) >= 0;

    public static bool operator ==(SemVersion? left, SemVersion? right) => left?.Equals(right) ?? right is null;

    public static bool operator !=(SemVersion? left, SemVersion? right) => !(left == right);

    /// <summary>Numeric identifiers compare as numbers and rank below alphanumeric ones, which compare as ASCII.</summary>
    private static int CompareIdentifiers(string a, string b) => (IsAllDigits(a), IsAllDigits(b)) switch
    {
        (true, true) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b),
        (true, false) => -1,
        (false, true) => 1,
        _ => string.CompareOrdinal(a, b),
    };

    /// <param name="numeric">Pre-release rules: a numeric identifier has no leading zero and at most nine digits.</param>
    private static bool AreIdentifiers(string text, bool numeric) =>
        text.Split('.').All(id =>
            id.Length > 0
            && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            && (!numeric || !IsAllDigits(id) || IsNumber(id)));

    private static bool TryNumber(string text, out int value)
    {
        value = 0;
        return IsNumber(text) && int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool IsNumber(string text) =>
        text.Length is > 0 and <= MaxNumberDigits && IsAllDigits(text) && (text.Length == 1 || text[0] != '0');

    private static bool IsAllDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
