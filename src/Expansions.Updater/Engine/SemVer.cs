using System.Globalization;

namespace Expansions.Updater.Engine;

/// <summary>
/// A semantic version, compared the way SemVer 2.0.0 says to.
/// <para>
/// String comparison is the wrong tool and gets this wrong in a way nobody notices until it matters:
/// ordinal comparison puts <c>1.10.0</c> before <c>1.9.0</c>, so an updater that used it would offer a
/// downgrade on the tenth minor release and then stop offering anything. The manifest contract makes
/// SemVer comparison an explicit requirement.
/// </para>
/// <para>
/// Build metadata (<c>+abc123</c>) is stripped rather than compared, per the specification â€” the .NET
/// SDK appends a source-revision id to <c>InformationalVersion</c> on some builds, and two builds of
/// one version must not read as different.
/// </para>
/// </summary>
internal readonly struct SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    internal static readonly SemVer None = default;

    private SemVer(int major, int minor, int patch, string preRelease, string text)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
        Text = text;
        IsValid = true;
    }

    internal int Major { get; }

    internal int Minor { get; }

    internal int Patch { get; }

    /// <summary>The dot-separated pre-release identifiers, empty for a release build.</summary>
    internal string PreRelease { get; }

    /// <summary>The version as parsed, build metadata removed. Empty when <see cref="IsValid"/> is false.</summary>
    internal string Text { get; }

    internal bool IsValid { get; }

    internal bool IsPreRelease => PreRelease.Length > 0;

    /// <summary>
    /// Accepts <c>1.2.3</c>, <c>1.2.3-rc.1</c>, a leading <c>v</c>, and a two-part <c>1.2</c>; also
    /// accepts the four-part <c>1.2.3.0</c> that <c>FileVersionInfo</c> hands back, treating the fourth
    /// part as build metadata. Anything else fails rather than guessing.
    /// </summary>
    internal static bool TryParse(string? value, out SemVer version)
    {
        version = None;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value!.Trim();

        if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V'))
            text = text[1..];

        var plus = text.IndexOf('+');
        if (plus >= 0)
            text = text[..plus];

        var preRelease = string.Empty;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = text[(dash + 1)..];
            text = text[..dash];
        }

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 4)
            return false;

        if (!TryPart(parts, 0, out var major) ||
            !TryPart(parts, 1, out var minor) ||
            !TryPart(parts, 2, out var patch))
        {
            return false;
        }

        // A fourth part only ever comes from a Windows file version, where it is a build counter.
        if (parts.Length == 4 && !TryPart(parts, 3, out _))
            return false;

        var canonical = preRelease.Length > 0
            ? $"{major}.{minor}.{patch}-{preRelease}"
            : $"{major}.{minor}.{patch}";

        version = new SemVer(major, minor, patch, preRelease, canonical);
        return true;
    }

    /// <summary>
    /// Compares two version strings. An unparseable version sorts below a parseable one, so a file
    /// whose version cannot be read is treated as older and therefore updatable, never as newer.
    /// </summary>
    internal static int Compare(string? left, string? right)
    {
        TryParse(left, out var a);
        TryParse(right, out var b);
        return a.CompareTo(b);
    }

    /// <summary>True when <paramref name="candidate"/> is strictly newer than <paramref name="current"/>.</summary>
    internal static bool IsNewer(string? candidate, string? current) => Compare(candidate, current) > 0;

    public int CompareTo(SemVer other)
    {
        if (!IsValid || !other.IsValid)
            return IsValid == other.IsValid ? 0 : IsValid ? 1 : -1;

        var byMajor = Major.CompareTo(other.Major);
        if (byMajor != 0)
            return byMajor;

        var byMinor = Minor.CompareTo(other.Minor);
        if (byMinor != 0)
            return byMinor;

        var byPatch = Patch.CompareTo(other.Patch);
        if (byPatch != 0)
            return byPatch;

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public bool Equals(SemVer other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemVer other && Equals(other);

    public override int GetHashCode() =>
        IsValid ? HashCode.Combine(Major, Minor, Patch, PreRelease) : 0;

    public override string ToString() => IsValid ? Text : string.Empty;

    /// <summary>
    /// SemVer Â§11.3â€“11.4: a pre-release version is lower than the release it precedes, identifiers are
    /// compared left to right, numeric identifiers compare numerically and rank below alphanumeric ones,
    /// and a shorter run of otherwise equal identifiers is lower.
    /// </summary>
    private static int ComparePreRelease(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0)
            return 0;

        if (left.Length == 0)
            return 1;

        if (right.Length == 0)
            return -1;

        var a = left.Split('.');
        var b = right.Split('.');
        var shared = Math.Min(a.Length, b.Length);

        for (var i = 0; i < shared; i++)
        {
            var leftNumeric = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

            int result;
            if (leftNumeric && rightNumeric)
                result = leftNumber.CompareTo(rightNumber);
            else if (leftNumeric)
                result = -1;
            else if (rightNumeric)
                result = 1;
            else
                result = string.CompareOrdinal(a[i], b[i]);

            if (result != 0)
                return result;
        }

        return a.Length.CompareTo(b.Length);
    }

    private static bool TryPart(string[] parts, int index, out int value)
    {
        if (index >= parts.Length)
        {
            // "1.2" is a version people write; the missing parts are zero.
            value = 0;
            return true;
        }

        return int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
