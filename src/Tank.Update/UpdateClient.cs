using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Tank.Update;

/// <summary>Fetches and verifies the manifest, then downloads a package and checks its size and SHA-256.</summary>
public sealed class UpdateClient
{
    public const string ManifestFile = "manifest.json";
    public const string SignatureFile = "manifest.sig";
    private const int MaxSignatureFileBytes = 2048;

    private readonly HttpClient _http;
    private readonly UpdateVerifier _verifier;
    private readonly Uri _feedBase;

    public UpdateClient(HttpClient http, UpdateVerifier verifier, Uri feedBase)
    {
        _http = http;
        _verifier = verifier;
        _feedBase = feedBase.AbsoluteUri.EndsWith('/') ? feedBase : new Uri(feedBase.AbsoluteUri + "/");
    }

    /// <summary>The verified manifest, or throws <see cref="UpdateException"/>.</summary>
    public async Task<UpdateManifest> FetchManifestAsync(CancellationToken ct)
    {
        // Cache-busting query: CDNs (raw.githubusercontent.com) may serve a stale manifest for minutes.
        string bust = "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] manifest = await GetLimitedAsync(new Uri(_feedBase, ManifestFile + bust), UpdateManifest.MaxManifestBytes, ct);
        byte[] sigFile = await GetLimitedAsync(new Uri(_feedBase, SignatureFile + bust), MaxSignatureFileBytes, ct);
        if (!_verifier.IsValid(manifest, UpdateVerifier.DecodeSignature(sigFile)))
            throw new UpdateException("Update manifest signature is invalid; ignoring it.");
        return UpdateManifest.Parse(manifest);
    }

    /// <summary>Streams the package to <paramref name="destFile"/>, verifying size and hash. Partial files never survive.</summary>
    public async Task DownloadAsync(UpdatePackage package, string destFile, IProgress<double>? progress, CancellationToken ct)
    {
        string part = destFile + ".part";
        try
        {
            using var response = await _http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new UpdateException($"Download failed: HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength is long announced && announced != package.Size)
                throw new UpdateException($"Download size {announced} does not match the manifest ({package.Size}).");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > package.Size) throw new UpdateException("Download is larger than the manifest says.");
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress?.Report((double)total / package.Size);
                }
                if (total != package.Size) throw new UpdateException($"Download ended early ({total} of {package.Size} bytes).");
            }
            string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (actual != package.Sha256) throw new UpdateException("Downloaded package does not match its SHA-256; discarded.");
            File.Move(part, destFile, overwrite: true);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"Download failed: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(part)) File.Delete(part);
        }
    }

    private async Task<byte[]> GetLimitedAsync(Uri uri, int limit, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new UpdateException($"Update feed returned HTTP {(int)response.StatusCode} for {uri.AbsolutePath}.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > limit) throw new UpdateException($"{uri.AbsolutePath} is unexpectedly large.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"Cannot reach the update feed: {ex.Message}", ex);
        }
    }
}
