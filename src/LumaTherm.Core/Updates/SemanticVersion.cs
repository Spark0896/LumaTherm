using System.Globalization;

namespace LumaTherm.Core.Updates;

public readonly record struct SemanticVersion : IComparable<SemanticVersion>
{
    public SemanticVersion(int major, int minor, int patch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public static SemanticVersion Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var literal = value.StartsWith('v') ? value[1..] : value;
        var components = literal.Split('.');
        if (components.Length != 3
            || !TryParseComponent(components[0], out var major)
            || !TryParseComponent(components[1], out var minor)
            || !TryParseComponent(components[2], out var patch))
        {
            throw new FormatException($"'{value}' is not a stable semantic version.");
        }

        return new SemanticVersion(major, minor, patch);
    }

    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    private static bool TryParseComponent(string value, out int component)
    {
        component = 0;
        if (value.Length == 0 || (value.Length > 1 && value[0] == '0')) return false;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out component);
    }
}
