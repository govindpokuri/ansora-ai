using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OverlayCopilot.Services;

public sealed record AppSettings
{
    [JsonIgnore] public CandidateProfile? Candidate { get; init; }
    public bool CommercialMode { get; init; }
    public double BackgroundOpacity { get; init; } = 1;
    public string CommercialBaseUrl { get; init; } = "";
    [JsonIgnore] public string CommercialSessionId { get; init; } = "";
    public string TranscriptionUrl { get; init; } = "";
    public string TranscriptionModel { get; init; } = "";
    public string AnswerUrl { get; init; } = "https://api.example.com/v1/chat/completions";
    public string AnswerModel { get; init; } = "gpt-oss-120b";
    public int MaxTokens { get; init; } = 4096;
    public double Temperature { get; init; } = 0.1;
    public string AnswerAuth { get; init; } = "api-key";
    public string TranscriptionAuth { get; init; } = "Bearer";
    public bool UseLocalSpeech { get; init; } = true;
    public bool UseMicrophone { get; init; }
    public bool UseBothAudio { get; init; }
    public string MicrophoneDeviceId { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string SystemPrompt { get; init; } = "Give a short, meaningful and accurate spoken answer in simple, natural Indian English. Usually use three to five short sentences with everyday words and only essential technical terms. Start with the answer and add one useful reason or small example if needed. Give more detail or code only when explicitly requested. If context is incomplete, briefly state what is missing. Do not invent personal experience.";

    public static bool IsLocalPreview
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory,"settings.defaults.json");
            if (!File.Exists(path)) return false;
            var defaults = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            return defaults?.CommercialMode == true && Uri.TryCreate(defaults.CommercialBaseUrl,UriKind.Absolute,out var origin) && origin.IsLoopback;
        }
    }
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnsoraAICommunity", IsLocalPreview ? "local-preview-settings.json" : File.Exists(Path.Combine(AppContext.BaseDirectory, "commercial.mode")) ? "commercial-settings.json" : "settings.json");
    public static AppSettings Load()
    {
        var defaultsPath = Path.Combine(AppContext.BaseDirectory, "settings.defaults.json");
        var defaults = File.Exists(defaultsPath)
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(defaultsPath)) ?? new() : new();
        var loaded = File.Exists(FilePath)
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? defaults : defaults;
        return Normalize(loaded, defaults);
    }
    public static AppSettings Normalize(AppSettings settings, AppSettings? defaults = null)
    {
        defaults ??= new();
        if (defaults.CommercialMode)
        {
            var baseUrl = (string.IsNullOrWhiteSpace(defaults.CommercialBaseUrl) ? settings.CommercialBaseUrl : defaults.CommercialBaseUrl).Trim().TrimEnd('/');
            if (baseUrl.Length > 0) ValidateEndpoint(baseUrl);
            return settings with { CommercialMode = true, CommercialBaseUrl = baseUrl,
                AnswerUrl = baseUrl.Length == 0 ? "" : baseUrl + "/api/chat/completions", AnswerAuth = "Bearer",
                UseMicrophone = false, UseBothAudio = false };
        }
        var url = string.IsNullOrWhiteSpace(settings.AnswerUrl) ? defaults.AnswerUrl : settings.AnswerUrl.Trim();
        return settings with { AnswerUrl = url, AnswerModel = string.IsNullOrWhiteSpace(settings.AnswerModel) ? defaults.AnswerModel : settings.AnswerModel,
            AnswerAuth = settings.AnswerAuth, UseMicrophone = false, UseBothAudio = false };
    }
    public void ValidateGeneration()
    {
        if (MaxTokens < 1 || MaxTokens > 32768) throw new ArgumentException("Max tokens must be between 1 and 32,768.");
        if (!double.IsFinite(Temperature) || Temperature < 0 || Temperature > 1)
            throw new ArgumentException("Temperature must be between 0 and 1 for this endpoint.");
    }
    public void ValidateAnswer()
    {
        ValidateEndpoint(AnswerUrl);
        ValidateGeneration();
        if (!CommercialMode && string.IsNullOrWhiteSpace(AnswerModel))
            throw new ArgumentException("Enter the AI deployment/model name in settings.");
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static Uri ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Use a full HTTPS endpoint URL (HTTP is allowed only on localhost). Do not put credentials in the URL.");
        return uri;
    }
}
