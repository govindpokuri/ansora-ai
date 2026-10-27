using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OverlayCopilot.Services;

public sealed record AppRelease(bool Enabled, string Version, string Url, string Sha256, long Size, string Notes);

public sealed class AppUpdates : IDisposable
{
    public static Version CurrentVersion => typeof(AppUpdates).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public const long MaxInstallerBytes = 400L * 1024 * 1024;
    private readonly HttpClient http;
    public AppUpdates(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    }
    public static Uri ValidateOrigin(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) || origin.Scheme != "https" || origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0 || origin.AbsolutePath != "/")
            throw new InvalidDataException("Updates require the HTTPS service website.");
        return origin;
    }
    public static void ValidateRelease(AppRelease release, Uri origin)
    {
        if (!System.Version.TryParse(release.Version, out var version) || version.Build < 0 || version.Revision > 0)
            throw new InvalidDataException("Invalid update version.");
        if (!Uri.TryCreate(release.Url, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length > 0 || url.Fragment.Length > 0 || url.Host != origin.Host || url.Port != origin.Port || !url.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update must come from the service website.");
        if (release.Size < 1 || release.Size > MaxInstallerBytes || release.Sha256.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(release.Sha256, "\\A[0-9a-fA-F]{64}\\z") || release.Notes.Length > 4000)
            throw new InvalidDataException("Invalid update size, checksum or notes.");
    }
    public async Task<AppRelease?> CheckAsync(string baseUrl, CancellationToken ct)
    {
        var origin = ValidateOrigin(baseUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "updates/windows.json"));
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var content = new MemoryStream();
        var buffer = new byte[4096]; int read;
        while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (content.Length + read > 16384) throw new InvalidDataException("Update manifest is too large.");
            content.Write(buffer, 0, read);
        }
        var release = JsonSerializer.Deserialize<AppRelease>(content.ToArray(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (release is null || !release.Enabled) return null;
        // Treat every manifest field as untrusted, including missing/null fields.
        if (release.Version is null || release.Url is null || release.Sha256 is null || release.Notes is null) throw new InvalidDataException("Incomplete update manifest.");
        ValidateRelease(release, origin);
        return new Version(release.Version) > CurrentVersion ? release : null;
    }
    public async Task<string> DownloadAsync(AppRelease release, string baseUrl, string directory, CancellationToken ct)
    {
        ValidateRelease(release, ValidateOrigin(baseUrl));
        if (new Version(release.Version) <= CurrentVersion) throw new InvalidDataException("An older update cannot be installed.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "AnsoraAI-Setup-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size) throw new InvalidDataException("Update download size does not match.");
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920]; long size = 0; int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                size += read;
                if (size > release.Size) throw new InvalidDataException("Update exceeds its declared size.");
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            if (size != release.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
                throw new InvalidDataException("Update verification failed. No installer was launched.");
            await destination.FlushAsync(ct).ConfigureAwait(false);
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    public void Dispose() => http.Dispose();
}
