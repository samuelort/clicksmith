using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Clicksmith.Core;

/// <summary>X.Y.Z with optional -prerelease (e.g. 2.4.0-beta.1). Prereleases sort before the release.</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.\-]+))?$");
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }

    private SemanticVersion(int a, int b, int c, string? pre) { Major = a; Minor = b; Patch = c; Prerelease = pre; }

    public static SemanticVersion? Parse(string? text)
    {
        if (text is null) return null;
        var m = Pattern.Match(text.Trim());
        if (!m.Success) return null;
        if (!int.TryParse(m.Groups[1].Value, out var a) || !int.TryParse(m.Groups[2].Value, out var b) || !int.TryParse(m.Groups[3].Value, out var c)) return null;
        return new SemanticVersion(a, b, c, m.Groups[4].Success ? m.Groups[4].Value : null);
    }

    public int CompareTo(SemanticVersion? o)
    {
        if (o is null) return 1;
        var c = (Major, Minor, Patch).CompareTo((o.Major, o.Minor, o.Patch));
        if (c != 0) return c;
        if (Prerelease is null) return o.Prerelease is null ? 0 : 1;
        if (o.Prerelease is null) return -1;
        var a = Prerelease.Split('.');
        var b = o.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var na = int.TryParse(a[i], out var ia);
            var nb = int.TryParse(b[i], out var ib);
            var r = na && nb ? ia.CompareTo(ib) : na ? -1 : nb ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (r != 0) return r;
        }
        return a.Length.CompareTo(b.Length);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Prerelease is null ? "" : "-" + Prerelease);
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message) { }
}

/// <summary>2.4: describes one release package. Verified offline before anything is installed.</summary>
public sealed class ReleaseManifest
{
    private static readonly Regex SafeName = new(@"^[A-Za-z0-9._\-]{1,120}\.zip$");
    private static readonly Regex Sha = new(@"^[a-f0-9]{64}$");

    public string Version { get; set; } = "";
    public string Artifact { get; set; } = "";
    public string Sha256 { get; set; } = "";

    public void Validate()
    {
        if (SemanticVersion.Parse(Version) is null) throw new UpdateException("Invalid version.");
        if (!SafeName.IsMatch(Artifact) || Artifact.Contains("..")) throw new UpdateException("Unsafe artifact name.");
        if (!Sha.IsMatch(Sha256)) throw new UpdateException("Invalid SHA-256.");
    }

    public void VerifyFile(string path, long maxBytes = 300_000_000)
    {
        Validate();
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new UpdateException("Package is missing or a link.");
        if (info.Length > maxBytes) throw new UpdateException("Package is too large.");
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (actual != Sha256) throw new UpdateException("Checksum mismatch — the package was changed or damaged.");
    }
}

public static class UpdatePolicy
{
    /// <summary>Is the release tag (e.g. windows-v2.4.1) newer than the running version?</summary>
    public static bool IsNewer(string? tag, string current)
    {
        if (tag is null || !tag.StartsWith(ClicksmithInfo.ReleaseTagPrefix, StringComparison.Ordinal)) return false;
        var remote = SemanticVersion.Parse(tag[ClicksmithInfo.ReleaseTagPrefix.Length..]);
        var local = SemanticVersion.Parse(current);
        return remote is not null && local is not null && remote.CompareTo(local) > 0;
    }
}
