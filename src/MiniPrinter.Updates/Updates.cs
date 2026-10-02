using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MiniPrinter.Updates;

/// <summary>A GitHub release asset.</summary>
public sealed record ReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
    [property: JsonPropertyName("size")] long Size);

/// <summary>The fields of a GitHub release (API v3) that the updater uses.</summary>
public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("assets")] IReadOnlyList<ReleaseAsset> Assets)
{
    public static GitHubRelease Parse(string json) =>
        JsonSerializer.Deserialize<GitHubRelease>(json) ?? throw new InvalidDataException("Empty release JSON.");
}

/// <summary>An update the user can install.</summary>
public sealed record UpdateOffer(Version Version, string Notes, string? ReleaseUrl, ReleaseAsset Installer, ReleaseAsset Checksums, ReleaseAsset Signature);

public static partial class UpdateSelector
{
    public const string ChecksumsName = "SHA256SUMS";
    public const string SignatureName = "SHA256SUMS.sig";

    /// <summary>Parses tags "vX.Y.Z" or "X.Y.Z"; returns null for anything else (including pre-release suffixes).</summary>
    public static Version? ParseTag(string? tag)
    {
        var match = TagPattern().Match(tag ?? "");
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>
    /// Returns the update offered by <paramref name="release"/>, or null when it is a draft or
    /// pre-release, not newer than <paramref name="current"/>, skipped by the user, or lacks the
    /// installer, checksums or signature assets.
    /// </summary>
    public static UpdateOffer? Select(GitHubRelease release, Version current, Version? skipped = null)
    {
        if (release.Draft || release.Prerelease)
            return null;
        var version = ParseTag(release.TagName);
        if (version is null || version <= Normalize(current) || (skipped is not null && version == Normalize(skipped)))
            return null;

        var installer = release.Assets.FirstOrDefault(a => a.Name.Equals($"MiniPrinter-Setup-{version}.exe", StringComparison.OrdinalIgnoreCase));
        var sums = release.Assets.FirstOrDefault(a => a.Name == ChecksumsName);
        var signature = release.Assets.FirstOrDefault(a => a.Name == SignatureName);
        if (installer is null || sums is null || signature is null)
            return null;
        return new UpdateOffer(version, release.Body ?? "", release.HtmlUrl, installer, sums, signature);
    }

    /// <summary>Version with exactly three components (assembly versions carry a fourth).</summary>
    public static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    [GeneratedRegex(@"^v?(\d+)\.(\d+)\.(\d+)$")]
    private static partial Regex TagPattern();
}

/// <summary>Parses <c>sha256sum</c>-style files: "&lt;hex&gt;  &lt;file&gt;" or "&lt;hex&gt; *&lt;file&gt;".</summary>
public static class Checksums
{
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var space = line.IndexOf(' ');
            if (space != 64)
                throw new InvalidDataException($"Malformed checksum line: '{line}'.");
            var hash = line[..64].ToLowerInvariant();
            if (!hash.All(Uri.IsHexDigit))
                throw new InvalidDataException($"Malformed checksum: '{hash}'.");
            var name = line[64..].TrimStart(' ', '*');
            result[name] = hash;
        }
        return result;
    }

    public static string Sha256Hex(Stream stream) => Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

    public static string Sha256Hex(string path)
    {
        using var file = File.OpenRead(path);
        return Sha256Hex(file);
    }

    /// <summary>Builds a SHA256SUMS file for the given files (used by the release pipeline).</summary>
    public static string Create(IEnumerable<string> paths) =>
        string.Concat(paths.Select(p => $"{Sha256Hex(p)}  {Path.GetFileName(p)}\n"));
}

/// <summary>ECDSA P-256 / SHA-256 signatures over release files (keys and signatures in Base64).</summary>
public static class ReleaseSignature
{
    /// <summary>Returns (private PKCS#8, public SubjectPublicKeyInfo), both Base64.</summary>
    public static (string PrivateKey, string PublicKey) GenerateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(key.ExportPkcs8PrivateKey()), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    public static string Sign(byte[] data, string privateKeyBase64)
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64.Trim()), out _);
        return Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256));
    }

    public static bool Verify(byte[] data, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64.Trim()), out _);
            return key.VerifyData(data, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }
}

public sealed class UpdateVerificationException(string message) : Exception(message);

/// <summary>Checks a downloaded installer against a signed SHA256SUMS.</summary>
public static class UpdateVerifier
{
    /// <exception cref="UpdateVerificationException">Signature or hash does not match.</exception>
    public static void Verify(string installerPath, string installerName, byte[] checksumsFile, string signatureBase64, string publicKeyBase64)
    {
        if (!ReleaseSignature.Verify(checksumsFile, signatureBase64, publicKeyBase64))
            throw new UpdateVerificationException("La firma de SHA256SUMS no es válida.");
        var sums = Checksums.Parse(Encoding.UTF8.GetString(checksumsFile));
        if (!sums.TryGetValue(installerName, out var expected))
            throw new UpdateVerificationException($"SHA256SUMS no incluye {installerName}.");
        if (!string.Equals(Checksums.Sha256Hex(installerPath), expected, StringComparison.OrdinalIgnoreCase))
            throw new UpdateVerificationException("El hash del instalador no coincide con SHA256SUMS.");
    }
}

/// <summary>Talks to the GitHub releases API and downloads verified updates.</summary>
public sealed class UpdateClient : IDisposable
{
    public const string Repository = "aleixlogin/miniprinter-win";

    private readonly HttpClient _http;
    private readonly string _publicKey;

    public UpdateClient(string publicKeyBase64, Version currentVersion, HttpMessageHandler? handler = null)
    {
        _publicKey = publicKeyBase64;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"MiniPrinter/{UpdateSelector.Normalize(currentVersion)}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.Timeout = TimeSpan.FromMinutes(10);
    }

    public async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken ct = default) =>
        GitHubRelease.Parse(await _http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct).ConfigureAwait(false));

    /// <summary>
    /// Downloads installer, checksums and signature into <paramref name="folder"/> and verifies them.
    /// Deletes everything and throws <see cref="UpdateVerificationException"/> when verification fails.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateOffer offer, string folder, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        var installerPath = Path.Combine(folder, offer.Installer.Name);
        try
        {
            var sums = await _http.GetByteArrayAsync(offer.Checksums.DownloadUrl, ct).ConfigureAwait(false);
            var signature = await _http.GetStringAsync(offer.Signature.DownloadUrl, ct).ConfigureAwait(false);

            using (var response = await _http.GetAsync(offer.Installer.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? offer.Installer.Size;
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(installerPath);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total > 0)
                        progress?.Report((double)done / total);
                }
            }

            UpdateVerifier.Verify(installerPath, offer.Installer.Name, sums, signature, _publicKey);
            return installerPath;
        }
        catch
        {
            TryDelete(installerPath);
            throw;
        }
    }

    public void Dispose() => _http.Dispose();

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
