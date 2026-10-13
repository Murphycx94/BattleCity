using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Tank.Update.Tests;

/// <summary>Test signing key, signed manifests, fake HTTP and release-shaped packages.</summary>
internal sealed class Fixtures : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public Fixtures()
    {
        Root = Path.Combine(Path.GetTempPath(), "tank-update-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string PublicPem => _key.ExportSubjectPublicKeyInfoPem();
    public FakeHttp Http { get; } = new();
    public static Uri Feed { get; } = new("https://feed.test/stable/");

    public byte[] Sign(byte[] data) => _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    public static string ManifestJson(string version, string url, byte[] package, string platform = "linux", string notes = "新增：测试") =>
        "{\"version\":\"" + version + "\",\"notes\":\"" + notes + "\",\"platforms\":{\"" + platform + "\":{\"url\":\"" + url +
        "\",\"sha256\":\"" + Sha(package) + "\",\"size\":" + package.Length + "}}}";

    /// <summary>Publishes a signed manifest (base64 signature, like the real feed) pointing at <paramref name="package"/>.</summary>
    public void PublishFeed(string version, byte[] package, string platform = "linux")
    {
        const string url = "https://downloads.test/pkg";
        var manifest = Encoding.UTF8.GetBytes(ManifestJson(version, url, package, platform));
        Http.Files[Feed + "manifest.json"] = manifest;
        Http.Files[Feed + "manifest.sig"] = Encoding.ASCII.GetBytes(Convert.ToBase64String(Sign(manifest)) + "\n");
        Http.Files[url] = package;
    }

    public static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>A Linux-style release: launcher script (writes a marker when run), pck and data dir.</summary>
    public static byte[] LinuxPackage(string label)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            AddFile(tar, "./", null, TarEntryType.Directory);
            AddFile(tar, "./Tank3D.x86_64", $"#!/bin/sh\necho {label} > \"$(dirname \"$0\")/launched.txt\"\n", TarEntryType.RegularFile,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            AddFile(tar, "./Tank3D.pck", label, TarEntryType.RegularFile);
            AddFile(tar, "./data_Tank.Client_linux_x86_64/", null, TarEntryType.Directory);
            AddFile(tar, "./data_Tank.Client_linux_x86_64/Tank.Client.dll", label, TarEntryType.RegularFile);
        }
        return ms.ToArray();
    }

    public static byte[] WindowsPackage(string label, string? extraEntry = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
            Add("Tank3D.exe", label);
            Add("Tank3D.pck", label);
            Add("data_Tank.Client_windows_x86_64/Tank.Client.dll", label);
            if (extraEntry is not null) Add(extraEntry, "evil");
        }
        return ms.ToArray();
    }

    private static void AddFile(TarWriter tar, string name, string? text, TarEntryType type, UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite)
    {
        var entry = new PaxTarEntry(type, name) { Mode = type == TarEntryType.Directory ? mode | UnixFileMode.UserExecute : mode };
        if (text is not null) entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        tar.WriteEntry(entry);
    }

    public void Dispose()
    {
        _key.Dispose();
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}

internal sealed class FakeHttp : HttpMessageHandler
{
    public Dictionary<string, byte[]> Files { get; } = new();
    public List<string> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string url = request.RequestUri!.GetLeftPart(UriPartial.Path);
        Requests.Add(url);
        var response = Files.TryGetValue(url, out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        return Task.FromResult(response);
    }
}
