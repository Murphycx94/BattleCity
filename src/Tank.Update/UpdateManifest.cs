using System.Text.Json;

namespace Tank.Update;

public sealed record UpdatePackage(string Url, string Sha256, long Size);

/// <summary>
/// The signed description of the newest release. Only trusted after <see cref="UpdateVerifier"/> accepted its
/// signature; parsing still validates everything because the bytes came off the network.
/// </summary>
public sealed record UpdateManifest(SemVer Version, string Notes, IReadOnlyDictionary<UpdatePlatform, UpdatePackage> Packages)
{
    public const int MaxManifestBytes = 16 * 1024;
    public const long MaxPackageBytes = 400L * 1024 * 1024;
    private const int MaxNotesChars = 200;

    public static UpdateManifest Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length == 0 || json.Length > MaxManifestBytes) throw new UpdateException("Update manifest has an invalid size.");
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json.ToArray());
        }
        catch (JsonException ex)
        {
            throw new UpdateException($"Update manifest is not valid JSON: {ex.Message}");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new UpdateException("Update manifest must be a JSON object.");
            if (!SemVer.TryParse(GetString(root, "version"), out var version)) throw new UpdateException("Update manifest has no valid version.");
            string notes = GetString(root, "notes") ?? string.Empty;
            if (notes.Length > MaxNotesChars) notes = notes[..MaxNotesChars];

            if (!root.TryGetProperty("platforms", out var platforms) || platforms.ValueKind != JsonValueKind.Object)
                throw new UpdateException("Update manifest has no platforms.");
            var packages = new Dictionary<UpdatePlatform, UpdatePackage>();
            foreach (var entry in platforms.EnumerateObject())
            {
                if (UpdatePlatformNames.FromKey(entry.Name) is not { } platform || entry.Value.ValueKind != JsonValueKind.Object) continue;
                packages[platform] = ParsePackage(entry.Name, entry.Value);
            }
            return new UpdateManifest(version, notes, packages);
        }
    }

    private static UpdatePackage ParsePackage(string name, JsonElement e)
    {
        string url = GetString(e, "url") ?? string.Empty;
        // https only; plain http is accepted for loopback so a local test feed works (the signature still applies).
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new UpdateException($"Package '{name}' must be downloaded over https.");
        string sha = (GetString(e, "sha256") ?? string.Empty).ToLowerInvariant();
        if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) throw new UpdateException($"Package '{name}' has an invalid sha256.");
        if (!e.TryGetProperty("size", out var sizeEl) || !sizeEl.TryGetInt64(out long size) || size <= 0 || size > MaxPackageBytes)
            throw new UpdateException($"Package '{name}' has an invalid size.");
        return new UpdatePackage(url, sha, size);
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message) { }

    public UpdateException(string message, Exception inner) : base(message, inner) { }
}
