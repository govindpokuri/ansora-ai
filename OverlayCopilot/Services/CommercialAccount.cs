using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OverlayCopilot.Services;

public sealed record CommercialSession(string BaseUrl, string Email, string Token, long ExpiresAt);
public sealed record CommercialBalance(string Email, long Available, long Reserved, bool AiConfigured);
public sealed record SignInCodeRequest(string Challenge, string Message);
public sealed class CommercialAccountStore
{
    private readonly string path;
    public CommercialAccountStore(string? path = null) => this.path = path ?? Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!,
        AppSettings.IsLocalPreview ? "local-preview-account.dpapi" : "commercial-account.dpapi");
    public CommercialSession? Load(string baseUrl)
    {
        if (!File.Exists(path)) return null;
        var data = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try
        {
            var value = JsonSerializer.Deserialize<CommercialSession>(data);
            return value is not null && value.BaseUrl.TrimEnd('/').Equals(baseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                && value.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? value : null;
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }
    public void Save(CommercialSession value)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(value);
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllBytes(path, ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }
    public void Clear() { if (File.Exists(path)) File.Delete(path); }
}
public sealed class CommercialAccountClient : IDisposable
{
    private readonly HttpClient http;
    public CommercialAccountClient(HttpMessageHandler? handler = null) => http = handler is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) } : new HttpClient(handler);
    private async Task<JsonDocument> Send(string baseUrl, string route, object? body, string token, CancellationToken ct)
    {
        AppSettings.ValidateEndpoint(baseUrl);
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/" + route);
        request.Headers.Add("X-Client", "desktop");
        if (token.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            try { using var error = JsonDocument.Parse(text); throw new InvalidOperationException(error.RootElement.GetProperty("error").GetString()); }
            catch (JsonException) { throw new HttpRequestException("Account service could not complete the request.", null, response.StatusCode); }
        }
        return JsonDocument.Parse(text);
    }
    public async Task<string> StartAsync(string url, string email, CancellationToken ct)
    {
        return (await StartDetailedAsync(url,email,ct)).Challenge;
    }
    public async Task<SignInCodeRequest> StartDetailedAsync(string url, string email, CancellationToken ct)
    {
        using var json = await Send(url, "auth/start", new { email, terms_accepted = true }, "", ct);
        var root = json.RootElement;
        var message = root.TryGetProperty("message",out var value) ? value.GetString() : null;
        return new(root.GetProperty("challenge").GetString()!, message ?? "Code requested. Check your inbox and spam folder; it expires in 10 minutes.");
    }
    public async Task<CommercialSession> VerifyAsync(string url, string challenge, string code, CancellationToken ct)
    {
        using var json = await Send(url, "auth/verify", new { challenge, code }, "", ct); var root = json.RootElement;
        return new(url.TrimEnd('/'), root.GetProperty("user").GetProperty("email").GetString()!, root.GetProperty("token").GetString()!, root.GetProperty("expires_at").GetInt64());
    }
    public async Task<CommercialBalance> BalanceAsync(CommercialSession session, CancellationToken ct)
    {
        using var json = await Send(session.BaseUrl, "account", null, session.Token, ct); var root = json.RootElement;
        return new(root.GetProperty("user").GetProperty("email").GetString()!, root.GetProperty("wallet").GetProperty("available").GetInt64(), root.GetProperty("wallet").GetProperty("reserved").GetInt64(), root.GetProperty("ai_configured").GetBoolean());
    }
    public async Task LogoutAsync(CommercialSession session, CancellationToken ct) { using var json = await Send(session.BaseUrl, "auth/logout", new { }, session.Token, ct); }
    public void Dispose() => http.Dispose();
}
