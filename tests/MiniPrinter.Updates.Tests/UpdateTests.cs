using System.Net;
using System.Text;

namespace MiniPrinter.Updates.Tests;

public class UpdateTests
{
    private static readonly (string Private, string Public) Keys = ReleaseSignature.GenerateKeyPair();

    private static GitHubRelease Release(string tag, bool draft = false, bool prerelease = false, bool withAssets = true)
    {
        var version = tag.TrimStart('v');
        var assets = withAssets
            ? new List<ReleaseAsset>
            {
                new($"MiniPrinter-Setup-{version}.exe", $"https://example.test/MiniPrinter-Setup-{version}.exe", 100),
                new("SHA256SUMS", "https://example.test/SHA256SUMS", 80),
                new("SHA256SUMS.sig", "https://example.test/SHA256SUMS.sig", 96),
            }
            : new List<ReleaseAsset>();
        return new GitHubRelease(tag, $"MiniPrinter {version}", "Novedades", draft, prerelease, "https://github.com/x", assets);
    }

    [Theory]
    [InlineData("v0.5.0", 0, 5, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    public void Parses_release_tags(string tag, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), UpdateSelector.ParseTag(tag));

    [Theory]
    [InlineData("v0.5.0-beta")]
    [InlineData("latest")]
    [InlineData("")]
    public void Rejects_other_tags(string tag) => Assert.Null(UpdateSelector.ParseTag(tag));

    [Fact]
    public void Offers_newer_release_only()
    {
        var current = new Version(0, 4, 0, 0);   // assembly versions have four parts
        Assert.NotNull(UpdateSelector.Select(Release("v0.5.0"), current));
        Assert.NotNull(UpdateSelector.Select(Release("v0.4.1"), current));
        Assert.Null(UpdateSelector.Select(Release("v0.4.0"), current));
        Assert.Null(UpdateSelector.Select(Release("v0.3.9"), current));
    }

    [Fact]
    public void Ignores_drafts_prereleases_skipped_and_incomplete_releases()
    {
        var current = new Version(0, 4, 0);
        Assert.Null(UpdateSelector.Select(Release("v0.5.0", draft: true), current));
        Assert.Null(UpdateSelector.Select(Release("v0.5.0", prerelease: true), current));
        Assert.Null(UpdateSelector.Select(Release("v0.5.0", withAssets: false), current));
        Assert.Null(UpdateSelector.Select(Release("v0.5.0"), current, skipped: new Version(0, 5, 0)));
        Assert.NotNull(UpdateSelector.Select(Release("v0.5.1"), current, skipped: new Version(0, 5, 0)));
    }

    [Fact]
    public void Parses_github_api_json()
    {
        const string json = """
            {"tag_name":"v0.5.0","name":"MiniPrinter 0.5.0","body":"- Algo nuevo","draft":false,"prerelease":false,
             "html_url":"https://github.com/aleixlogin/miniprinter-win/releases/tag/v0.5.0",
             "assets":[{"name":"MiniPrinter-Setup-0.5.0.exe","browser_download_url":"https://github.com/x/MiniPrinter-Setup-0.5.0.exe","size":9437184},
                       {"name":"SHA256SUMS","browser_download_url":"https://github.com/x/SHA256SUMS","size":95},
                       {"name":"SHA256SUMS.sig","browser_download_url":"https://github.com/x/SHA256SUMS.sig","size":96}]}
            """;
        var offer = UpdateSelector.Select(GitHubRelease.Parse(json), new Version(0, 4, 0));
        Assert.NotNull(offer);
        Assert.Equal(new Version(0, 5, 0), offer.Version);
        Assert.Equal("- Algo nuevo", offer.Notes);
        Assert.Equal(9437184, offer.Installer.Size);
    }

    [Fact]
    public void Checksums_parse_both_formats_and_reject_garbage()
    {
        var hash = new string('a', 64);
        var sums = Checksums.Parse($"{hash}  MiniPrinter-Setup-0.5.0.exe\n{new string('b', 64)} *other.bin\n\n");
        Assert.Equal(hash, sums["MiniPrinter-Setup-0.5.0.exe"]);
        Assert.Equal(new string('b', 64), sums["other.bin"]);
        Assert.Throws<InvalidDataException>(() => Checksums.Parse("nothex  file"));
    }

    [Fact]
    public void Signature_round_trip_and_tampering()
    {
        var data = Encoding.UTF8.GetBytes("abc  file\n");
        var signature = ReleaseSignature.Sign(data, Keys.Private);
        Assert.True(ReleaseSignature.Verify(data, signature, Keys.Public));
        Assert.False(ReleaseSignature.Verify(Encoding.UTF8.GetBytes("abd  file\n"), signature, Keys.Public));
        var other = ReleaseSignature.GenerateKeyPair();
        Assert.False(ReleaseSignature.Verify(data, signature, other.PublicKey));
        Assert.False(ReleaseSignature.Verify(data, "not base64!", Keys.Public));
    }

    private static (string Dir, string Installer, byte[] Sums, string Signature) SignedRelease(string version = "0.5.0")
    {
        var dir = Path.Combine(Path.GetTempPath(), "miniprinter-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var installer = Path.Combine(dir, $"MiniPrinter-Setup-{version}.exe");
        File.WriteAllBytes(installer, Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray());
        var sums = Encoding.UTF8.GetBytes(Checksums.Create([installer]));
        return (dir, installer, sums, ReleaseSignature.Sign(sums, Keys.Private));
    }

    [Fact]
    public void Verifier_accepts_signed_installer_and_rejects_tampering()
    {
        var (_, installer, sums, signature) = SignedRelease();
        var name = Path.GetFileName(installer);
        UpdateVerifier.Verify(installer, name, sums, signature, Keys.Public);

        File.AppendAllText(installer, "x"); // tampered installer
        Assert.Throws<UpdateVerificationException>(() => UpdateVerifier.Verify(installer, name, sums, signature, Keys.Public));

        var forgedSums = Encoding.UTF8.GetBytes(Checksums.Create([installer])); // attacker updates SHA256SUMS…
        Assert.Throws<UpdateVerificationException>(() => UpdateVerifier.Verify(installer, name, forgedSums, signature, Keys.Public)); // …but cannot re-sign
    }

    /// <summary>Serves files from a dictionary of URL → bytes.</summary>
    private sealed class FakeHandler(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Client_downloads_and_verifies_or_deletes()
    {
        var (_, installer, sums, signature) = SignedRelease();
        var offer = UpdateSelector.Select(Release("v0.5.0"), new Version(0, 4, 0))!;
        var target = Path.Combine(Path.GetTempPath(), "miniprinter-update-tests", Guid.NewGuid().ToString("N"));
        var files = new Dictionary<string, byte[]>
        {
            [offer.Installer.DownloadUrl] = File.ReadAllBytes(installer),
            [offer.Checksums.DownloadUrl] = sums,
            [offer.Signature.DownloadUrl] = Encoding.ASCII.GetBytes(signature),
        };
        using (var client = new UpdateClient(Keys.Public, new Version(0, 4, 0), new FakeHandler(files)))
        {
            var progress = new List<double>();
            var path = await client.DownloadAsync(offer, target, new Progress<double>(progress.Add));
            Assert.True(File.Exists(path));
        }

        files[offer.Installer.DownloadUrl] = [1, 2, 3]; // served installer does not match
        var bad = Path.Combine(target, "bad");
        using (var client = new UpdateClient(Keys.Public, new Version(0, 4, 0), new FakeHandler(files)))
        {
            await Assert.ThrowsAsync<UpdateVerificationException>(() => client.DownloadAsync(offer, bad));
            Assert.False(File.Exists(Path.Combine(bad, offer.Installer.Name)));
        }
    }
}
