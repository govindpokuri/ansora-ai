using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OverlayCopilot.Services;

public sealed class CredentialStore
{
    private sealed record Credential(string Url, string Auth, string Key);
    private readonly string path;
    public CredentialStore(string? path = null) => this.path = path ?? Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "answer-key.dpapi");
    public string Load(string url, string auth)
    {
        if (!File.Exists(path)) return "";
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try
        {
            var value = JsonSerializer.Deserialize<Credential>(plain);
            return value is not null && MatchesResource(value.Url, url) && value.Auth == auth ? value.Key : "";
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Save(string url, string auth, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) { Forget(); return; }
        var plain = JsonSerializer.SerializeToUtf8Bytes(new Credential(url, auth, key.Trim()));
        try
        {
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, encrypted);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Forget() { if (File.Exists(path)) File.Delete(path); }
    public void ExportTestPackage(string destination, string url, string auth)
    {
        var key = Load(url, auth);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No matching saved test credential is available.");
        File.WriteAllText(destination, JsonSerializer.Serialize(new Credential(url, auth, key)));
    }
    public bool ImportTestPackage(string packageFile, string url, string auth)
    {
        if (!File.Exists(packageFile)) return false;
        var value = JsonSerializer.Deserialize<Credential>(File.ReadAllText(packageFile));
        if (value is null || string.IsNullOrWhiteSpace(value.Key) || value.Auth != auth || !MatchesResource(value.Url, url)) return false;
        Save(url, auth, value.Key); return true;
    }
    private static bool MatchesResource(string saved, string requested)
    {
        var left = AppSettings.ValidateEndpoint(saved);
        var right = AppSettings.ValidateEndpoint(requested);
        // Azure keys belong to a resource, not an API-version query string.
        return left.Scheme == right.Scheme && left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase)
            && left.Port == right.Port && (!left.IsLoopback || left.PathAndQuery == right.PathAndQuery);
    }
}
