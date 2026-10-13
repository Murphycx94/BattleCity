using System.Diagnostics;
using System.Text;

namespace Tank.Update.Tests;

public sealed class SemVerTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.1")]
    [InlineData("0.1.9", "0.2.0")]
    [InlineData("0.2.0-rc.1", "0.2.0")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10")]
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    [InlineData("v1.2.3", "1.10.0")]
    public void Ordering(string older, string newer)
    {
        Assert.True(SemVer.Parse(newer) > SemVer.Parse(older));
        Assert.True(SemVer.Parse(older) < SemVer.Parse(newer));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData(null)]
    public void RejectsInvalid(string? text) => Assert.False(SemVer.TryParse(text, out _));

    [Fact]
    public void RoundTrips() => Assert.Equal("1.2.3-rc.1", SemVer.Parse("1.2.3-rc.1").ToString());
}

public sealed class ManifestAndSignatureTests
{
    [Fact]
    public void Parse_ReadsPackages_AndIgnoresUnknownPlatforms()
    {
        var pkg = new byte[] { 1, 2, 3 };
        string json = Fixtures.ManifestJson("0.2.0", "https://x.test/a.zip", pkg).Replace("}}}", "},\"amiga\":{}}}");
        var m = UpdateManifest.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal(SemVer.Parse("0.2.0"), m.Version);
        Assert.Equal(Fixtures.Sha(pkg), m.Packages[UpdatePlatform.Linux].Sha256);
        Assert.Single(m.Packages);
    }

    [Fact]
    public void Parse_AllowsHttpOnlyForLoopback()
    {
        string json = Fixtures.ManifestJson("0.2.0", "http://127.0.0.1:8765/a.zip", new byte[] { 1 });
        Assert.Single(UpdateManifest.Parse(Encoding.UTF8.GetBytes(json)).Packages);
    }

    [Theory]
    [InlineData("http://x.test/a.zip", "https")]
    [InlineData("not a url", "https")]
    public void Parse_RejectsNonHttpsDownloads(string url, string expected)
    {
        string json = Fixtures.ManifestJson("0.2.0", url, new byte[] { 1 });
        var ex = Assert.Throws<UpdateException>(() => UpdateManifest.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("{\"version\":\"x\",\"platforms\":{}}")]
    [InlineData("[]")]
    [InlineData("{not json")]
    [InlineData("{\"version\":\"1.0.0\",\"platforms\":{\"linux\":{\"url\":\"https://a.test/x\",\"sha256\":\"zz\",\"size\":1}}}")]
    [InlineData("{\"version\":\"1.0.0\",\"platforms\":{\"linux\":{\"url\":\"https://a.test/x\",\"sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\",\"size\":99999999999}}}")]
    public void Parse_RejectsBadManifests(string json) =>
        Assert.Throws<UpdateException>(() => UpdateManifest.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Signature_AcceptsOriginal_RejectsTamperingAndOtherKeys()
    {
        using var f = new Fixtures();
        using var other = new Fixtures();
        var manifest = Encoding.UTF8.GetBytes("{\"version\":\"1.0.0\"}");
        var sig = f.Sign(manifest);
        var tampered = Encoding.UTF8.GetBytes("{\"version\":\"9.0.0\"}");

        Assert.True(new UpdateVerifier(f.PublicPem).IsValid(manifest, sig));
        Assert.False(new UpdateVerifier(f.PublicPem).IsValid(tampered, sig));
        Assert.False(new UpdateVerifier(other.PublicPem).IsValid(manifest, sig));
        Assert.False(new UpdateVerifier(f.PublicPem).IsValid(manifest, new byte[] { 1, 2, 3 }));
        Assert.False(new UpdateVerifier("garbage").IsValid(manifest, sig));
        Assert.Equal(sig, UpdateVerifier.DecodeSignature(Encoding.ASCII.GetBytes(Convert.ToBase64String(sig) + "\n")));
    }
}

public sealed class UpdateClientTests
{
    [Fact]
    public async Task FetchManifest_VerifiesSignature()
    {
        using var f = new Fixtures();
        f.PublishFeed("0.2.0", new byte[] { 9 });
        var client = new UpdateClient(new HttpClient(f.Http), new UpdateVerifier(f.PublicPem), Fixtures.Feed);
        Assert.Equal(SemVer.Parse("0.2.0"), (await client.FetchManifestAsync(default)).Version);

        f.Http.Files[Fixtures.Feed + "manifest.json"] = Encoding.UTF8.GetBytes(Fixtures.ManifestJson("9.9.9", "https://evil.test/x", new byte[] { 1 }));
        await Assert.ThrowsAsync<UpdateException>(() => client.FetchManifestAsync(default));
    }

    [Fact]
    public async Task Download_ChecksHash_AndLeavesNothingOnFailure()
    {
        using var f = new Fixtures();
        var good = new byte[] { 1, 2, 3, 4 };
        var client = new UpdateClient(new HttpClient(f.Http), new UpdateVerifier(f.PublicPem), Fixtures.Feed);
        string dest = Path.Combine(f.Root, "pkg");
        f.Http.Files["https://downloads.test/pkg"] = good;

        await client.DownloadAsync(new UpdatePackage("https://downloads.test/pkg", Fixtures.Sha(good), good.Length), dest, null, default);
        Assert.Equal(good, File.ReadAllBytes(dest));

        File.Delete(dest);
        f.Http.Files["https://downloads.test/pkg"] = new byte[] { 1, 2, 3, 5 };
        await Assert.ThrowsAsync<UpdateException>(() =>
            client.DownloadAsync(new UpdatePackage("https://downloads.test/pkg", Fixtures.Sha(good), good.Length), dest, null, default));
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task Download_RejectsWrongSizeAndMissingFiles()
    {
        using var f = new Fixtures();
        var client = new UpdateClient(new HttpClient(f.Http), new UpdateVerifier(f.PublicPem), Fixtures.Feed);
        f.Http.Files["https://downloads.test/pkg"] = new byte[] { 1, 2 };
        await Assert.ThrowsAsync<UpdateException>(() =>
            client.DownloadAsync(new UpdatePackage("https://downloads.test/pkg", new string('0', 64), 10), Path.Combine(f.Root, "a"), null, default));
        await Assert.ThrowsAsync<UpdateException>(() =>
            client.DownloadAsync(new UpdatePackage("https://downloads.test/none", new string('0', 64), 2), Path.Combine(f.Root, "b"), null, default));
    }
}

public sealed class ArchiveAndInstallTests
{
    [Fact]
    public void Extract_LinuxTarGz_KeepsExecutableBit()
    {
        using var f = new Fixtures();
        string archive = Path.Combine(f.Root, "pkg.tar.gz"), dest = Path.Combine(f.Root, "staged");
        File.WriteAllBytes(archive, Fixtures.LinuxPackage("new"));

        UpdateArchive.Extract(UpdatePlatform.Linux, archive, dest);

        Assert.True(File.Exists(Path.Combine(dest, "data_Tank.Client_linux_x86_64", "Tank.Client.dll")));
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(dest, "Tank3D.x86_64")).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void Extract_RejectsZipSlip_AndWrongLayout()
    {
        using var f = new Fixtures();
        string evil = Path.Combine(f.Root, "evil.zip"), odd = Path.Combine(f.Root, "odd.tar.gz");
        File.WriteAllBytes(evil, Fixtures.WindowsPackage("x", extraEntry: "../../escape.txt"));
        File.WriteAllBytes(odd, Fixtures.LinuxPackage("x"));

        Assert.Throws<UpdateException>(() => UpdateArchive.Extract(UpdatePlatform.Windows, evil, Path.Combine(f.Root, "s1")));
        Assert.False(File.Exists(Path.Combine(f.Root, "..", "escape.txt")));
        Assert.Throws<UpdateException>(() => UpdateArchive.Extract(UpdatePlatform.Windows, odd, Path.Combine(f.Root, "s2")));
    }

    [Fact]
    public void Extract_WindowsZip()
    {
        using var f = new Fixtures();
        string zip = Path.Combine(f.Root, "w.zip"), dest = Path.Combine(f.Root, "staged");
        File.WriteAllBytes(zip, Fixtures.WindowsPackage("new"));
        UpdateArchive.Extract(UpdatePlatform.Windows, zip, dest);
        Assert.Equal(new[] { "Tank3D.exe", "Tank3D.pck", "data_Tank.Client_windows_x86_64" }, UpdateArchive.PayloadItems(UpdatePlatform.Windows, dest).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Locate_FindsInstallForEachPlatform()
    {
        var mac = InstallTarget.Locate(UpdatePlatform.MacOS, "/Applications/My Tank.app/Contents/MacOS/Tank 3D")!;
        Assert.Equal("/Applications", mac.Directory);
        Assert.Equal("/Applications/My Tank.app", mac.RelaunchPath);
        Assert.Equal(new SwapItem("Tank 3D.app", "My Tank.app", true), mac.Items("/tmp/x").Single());
        Assert.Null(InstallTarget.Locate(UpdatePlatform.MacOS, "/usr/local/bin/godot"));

        var linux = InstallTarget.Locate(UpdatePlatform.Linux, "/home/me/games/tank/Tank3D.x86_64")!;
        Assert.Equal("/home/me/games/tank", linux.Directory);
    }

    [Fact]
    public void WindowsScript_MovesEveryItemAndRelaunches()
    {
        using var f = new Fixtures();
        string zip = Path.Combine(f.Root, "w.zip"), staged = Path.Combine(f.Root, "staged");
        File.WriteAllBytes(zip, Fixtures.WindowsPackage("new"));
        UpdateArchive.Extract(UpdatePlatform.Windows, zip, staged);
        var target = new InstallTarget(UpdatePlatform.Windows, @"C:\Games\Tank", @"C:\Games\Tank\Tank3D.exe", @"C:\Games\Tank\Tank3D.exe");

        string script = SwapScript.Build(target, staged, 4242, @"C:\log.txt");

        Assert.Contains("PID eq 4242", script);
        Assert.Contains("robocopy \"%STAGE%\\data_Tank.Client_windows_x86_64\"", script);
        Assert.Contains("move /y \"%STAGE%\\Tank3D.exe\" \"%TARGET%\\Tank3D.exe\"", script);
        Assert.Contains(":restore", script);
        Assert.Contains("start \"\" \"C:\\Games\\Tank\\Tank3D.exe\"", script);
    }
}

/// <summary>Runs the generated POSIX swap script for real (Linux layout; works on macOS too).</summary>
public sealed class SwapScriptExecutionTests
{
    private static (string install, string staged) Prepare(Fixtures f)
    {
        string install = Path.Combine(f.Root, "install with 'quote'"), staged = Path.Combine(f.Root, "staged-0.2.0");
        string archiveOld = Path.Combine(f.Root, "old.tar.gz"), archiveNew = Path.Combine(f.Root, "new.tar.gz");
        File.WriteAllBytes(archiveOld, Fixtures.LinuxPackage("old"));
        File.WriteAllBytes(archiveNew, Fixtures.LinuxPackage("new"));
        UpdateArchive.Extract(UpdatePlatform.Linux, archiveOld, install);
        UpdateArchive.Extract(UpdatePlatform.Linux, archiveNew, staged);
        File.WriteAllText(Path.Combine(install, "my-notes.txt"), "user file"); // must survive
        return (install, staged);
    }

    private static int RunScript(string script)
    {
        var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        psi.ArgumentList.Add(script);
        using var p = Process.Start(psi)!;
        p.WaitForExit(20_000);
        return p.ExitCode;
    }

    private static string WaitForLaunch(string install)
    {
        string marker = Path.Combine(install, "launched.txt");
        for (int i = 0; i < 100 && !File.Exists(marker); i++) Thread.Sleep(50);
        return File.Exists(marker) ? File.ReadAllText(marker).Trim() : "(not launched)";
    }

    [Fact]
    public void Swap_InstallsNewVersion_KeepsUserFiles_AndRelaunches()
    {
        if (OperatingSystem.IsWindows()) return;
        using var f = new Fixtures();
        var (install, staged) = Prepare(f);
        var target = InstallTarget.Locate(UpdatePlatform.Linux, Path.Combine(install, "Tank3D.x86_64"))!;
        string script = Path.Combine(f.Root, "apply.sh");
        File.WriteAllText(script, SwapScript.Build(target, staged, pid: 999_999, Path.Combine(f.Root, "update.log")));

        Assert.Equal(0, RunScript(script));

        Assert.Equal("new", WaitForLaunch(install));
        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "Tank3D.pck")));
        Assert.Equal("user file", File.ReadAllText(Path.Combine(install, "my-notes.txt")));
        Assert.False(Directory.Exists(Path.Combine(install, SwapScript.BackupDirName)));
        Assert.False(Directory.Exists(staged));
    }

    [Fact]
    public void Swap_RestoresOldVersion_WhenNewFilesAreMissing()
    {
        if (OperatingSystem.IsWindows()) return;
        using var f = new Fixtures();
        var (install, staged) = Prepare(f);
        var target = InstallTarget.Locate(UpdatePlatform.Linux, Path.Combine(install, "Tank3D.x86_64"))!;
        string script = Path.Combine(f.Root, "apply.sh");
        File.WriteAllText(script, SwapScript.Build(target, staged, pid: 999_999, Path.Combine(f.Root, "update.log")));
        File.Delete(Path.Combine(staged, "Tank3D.pck")); // simulate a broken staging dir after the script was generated

        Assert.NotEqual(0, RunScript(script));

        Assert.Equal("old", WaitForLaunch(install));
        Assert.Equal("old", File.ReadAllText(Path.Combine(install, "Tank3D.pck")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(install, "data_Tank.Client_linux_x86_64", "Tank.Client.dll")));
    }
}

public sealed class UpdaterTests
{
    private static Updater NewUpdater(Fixtures f, string current, string installDir)
    {
        var config = new UpdateConfig(Fixtures.Feed, f.PublicPem, SemVer.Parse(current), UpdatePlatform.Linux,
            Path.Combine(f.Root, "work"), Path.Combine(installDir, "Tank3D.x86_64"));
        return new Updater(config, new HttpClient(f.Http));
    }

    private static string Install(Fixtures f, string label)
    {
        string dir = Path.Combine(f.Root, "install");
        string archive = Path.Combine(f.Root, label + ".tar.gz");
        File.WriteAllBytes(archive, Fixtures.LinuxPackage(label));
        UpdateArchive.Extract(UpdatePlatform.Linux, archive, dir);
        return dir;
    }

    [Fact]
    public async Task NewerVersion_IsDownloadedStagedAndInstallable()
    {
        using var f = new Fixtures();
        string install = Install(f, "old");
        f.PublishFeed("0.2.0", Fixtures.LinuxPackage("new"));
        var updater = NewUpdater(f, "0.1.1", install);

        await updater.RunAsync(default);

        Assert.Equal(UpdateStatus.Ready, updater.Status);
        Assert.Equal(SemVer.Parse("0.2.0"), updater.Available!.Version);
        var (program, args, script) = updater.PrepareInstall(pid: 999_999);
        Assert.Equal("/bin/sh", program);
        Assert.Equal(script, args.Single());
        Assert.Contains("PID=999999", File.ReadAllText(script));
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.3.0")]
    public async Task SameOrOlderVersion_IsNotInstalled(string current)
    {
        using var f = new Fixtures();
        f.PublishFeed("0.2.0", Fixtures.LinuxPackage("new"));
        var updater = NewUpdater(f, current, Install(f, "old"));
        await updater.RunAsync(default);
        Assert.Equal(UpdateStatus.UpToDate, updater.Status);
        Assert.DoesNotContain("https://downloads.test/pkg", f.Http.Requests);
    }

    [Fact]
    public async Task BadSignature_FailsWithoutDownloading()
    {
        using var f = new Fixtures();
        using var attacker = new Fixtures();
        attacker.PublishFeed("9.9.9", Fixtures.LinuxPackage("evil"));
        foreach (var (k, v) in attacker.Http.Files) f.Http.Files[k] = v;
        var updater = NewUpdater(f, "0.1.0", Install(f, "old"));

        await updater.RunAsync(default);

        Assert.Equal(UpdateStatus.Failed, updater.Status);
        Assert.Contains("signature", updater.Error);
        Assert.DoesNotContain("https://downloads.test/pkg", f.Http.Requests);
    }

    [Fact]
    public async Task OtherPlatformOnly_IsUpToDate()
    {
        using var f = new Fixtures();
        f.PublishFeed("0.2.0", Fixtures.WindowsPackage("new"), platform: "windows");
        var updater = NewUpdater(f, "0.1.0", Install(f, "old"));
        await updater.RunAsync(default);
        Assert.Equal(UpdateStatus.UpToDate, updater.Status);
    }

    [Fact]
    public async Task StagedUpdate_SurvivesRestartWithoutRedownloading()
    {
        using var f = new Fixtures();
        string install = Install(f, "old");
        f.PublishFeed("0.2.0", Fixtures.LinuxPackage("new"));
        await NewUpdater(f, "0.1.1", install).RunAsync(default);
        f.Http.Requests.Clear();

        var again = NewUpdater(f, "0.1.1", install);
        Assert.Null(again.ConsumeJustUpdatedFrom());
        await again.RunAsync(default);

        Assert.Equal(UpdateStatus.Ready, again.Status);
        Assert.Empty(f.Http.Requests);
    }

    [Fact]
    public async Task AfterInstall_ReportsUpdate_AndCleansStaging()
    {
        using var f = new Fixtures();
        string install = Install(f, "old");
        f.PublishFeed("0.2.0", Fixtures.LinuxPackage("new"));
        var before = NewUpdater(f, "0.1.1", install);
        before.ConsumeJustUpdatedFrom();
        await before.RunAsync(default);

        var after = NewUpdater(f, "0.2.0", install); // as if the swap happened and the new build started
        Assert.Equal(SemVer.Parse("0.1.1"), after.ConsumeJustUpdatedFrom());
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(f.Root, "work"), "staged-*"));
        Assert.Null(after.ConsumeJustUpdatedFrom());
    }

    [Fact]
    public void PrepareInstall_WithoutReadyUpdate_Throws()
    {
        using var f = new Fixtures();
        Assert.Throws<InvalidOperationException>(() => NewUpdater(f, "0.1.0", Install(f, "old")).PrepareInstall(1));
    }
}

/// <summary>The release pipeline signs with the openssl CLI; the game must accept exactly that format.</summary>
public sealed class OpenSslCompatibilityTests
{
    [Fact]
    public void OpenSslSignature_VerifiesWithGameVerifier()
    {
        using var f = new Fixtures();
        string key = Path.Combine(f.Root, "key.pem"), data = Path.Combine(f.Root, "manifest.json"), sig = Path.Combine(f.Root, "manifest.sig");
        if (!Run("openssl", "genpkey", "-algorithm", "EC", "-pkeyopt", "ec_paramgen_curve:P-256", "-out", key)) return; // no openssl: skip
        File.WriteAllText(data, "{\"version\":\"1.0.0\"}");
        Assert.True(Run("openssl", "dgst", "-sha256", "-sign", key, "-out", sig, data));
        string pub = RunOutput("openssl", "pkey", "-in", key, "-pubout");

        var verifier = new UpdateVerifier(pub);
        Assert.True(verifier.IsValid(File.ReadAllBytes(data), File.ReadAllBytes(sig)));
        Assert.True(verifier.IsValid(File.ReadAllBytes(data), UpdateVerifier.DecodeSignature(Encoding.ASCII.GetBytes(Convert.ToBase64String(File.ReadAllBytes(sig))))));
    }

    [Fact]
    public void EmbeddedPublicKey_IsAValidP256Key()
    {
        using var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(UpdateDefaults.PublicKeyPem);
        Assert.Equal(256, key.KeySize);
        Assert.StartsWith("https://", UpdateDefaults.FeedUrl);
    }

    private static bool Run(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string RunOutput(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }
}
