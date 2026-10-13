namespace Tank.Update;

/// <summary>Minimal semantic version: X.Y.Z with an optional pre-release suffix (X.Y.Z-rc.1).</summary>
public readonly record struct SemVer(int Major, int Minor, int Patch, string PreRelease) : IComparable<SemVer>
{
    public static bool TryParse(string? text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string core = text.Trim().TrimStart('v'), pre = string.Empty;
        int dash = core.IndexOf('-');
        if (dash >= 0)
        {
            pre = core[(dash + 1)..];
            core = core[..dash];
            if (pre.Length == 0) return false;
        }
        var parts = core.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor) || !int.TryParse(parts[2], out int patch)) return false;
        if (major < 0 || minor < 0 || patch < 0) return false;
        version = new SemVer(major, minor, patch, pre);
        return true;
    }

    public static SemVer Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"'{text}' is not a version like 1.2.3 or 1.2.3-rc.1.");

    /// <summary>Release ordering: 1.0.0-rc.1 &lt; 1.0.0 &lt; 1.0.1. Pre-release identifiers compare numerically when numeric.</summary>
    public int CompareTo(SemVer other)
    {
        int c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (PreRelease.Length == 0 && other.PreRelease.Length == 0) return 0;
        if (PreRelease.Length == 0) return 1;  // a release is newer than its pre-releases
        if (other.PreRelease.Length == 0) return -1;
        var a = PreRelease.Split('.');
        var b = other.PreRelease.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = int.TryParse(a[i], out int ai), bn = int.TryParse(b[i], out int bi);
            c = an && bn ? ai.CompareTo(bi) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;

    public override string ToString() => PreRelease.Length == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}
