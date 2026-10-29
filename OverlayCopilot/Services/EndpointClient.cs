using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace OverlayCopilot.Services;

public sealed class EndpointClient : IDisposable
{
    public sealed record ChatTurn(string role, string content);
    public async Task AnswerToCompletionAsync(string question, AppSettings settings, string key, Action<string> append, CancellationToken ct,
        Action<TokenUsage>? reportUsage = null, Action<string,string>? reportMetadata = null, Action<bool>? reportIncompleteUsage = null)
    {
        var text = new StringBuilder(); TokenUsage? total = null; bool incomplete = false;
        var instructions = AnswerPresentation.ComposePrompt(settings.SystemPrompt, settings.Candidate, question);
        string model = "", finish = "";
        for (var round = 0; round < 3; round++)
        {
            ct.ThrowIfCancellationRequested(); finish = ""; TokenUsage? current = null;
            IReadOnlyList<ChatTurn>? conversation = round == 0 ? null : new[] {
                new ChatTurn("system", instructions), new ChatTurn("user", question), new ChatTurn("assistant", text.ToString()),
                new ChatTurn("user", "Continue exactly from the last character of the unfinished answer. Output only the missing remainder. Do not repeat previous text or add an introduction. Preserve the original coding language and continue inside any open code fence without opening it again; close it when the code is complete.") };
            try
            {
                await AnswerAsync(question, settings, key, chunk => { text.Append(chunk); append(chunk); }, ct,
                    usage => { current = usage; reportUsage?.Invoke(Sum(total, usage)); }, instructions,
                    (m,f) => { if(m.Length > 0) model=m; if(f.Length > 0) finish=f; reportMetadata?.Invoke(m,f); },
                    conversation, round > 0 ? model : "", value => { if(value) incomplete=true; });
            }
            finally
            {
                if(current is not null)total=Sum(total,current); else incomplete=true;
                reportIncompleteUsage?.Invoke(incomplete);
            }
            if(finish != "length")return;
        }
        // Bounded continuation prevents an endless paid generation loop. Caller retains length status.
    }
    private static TokenUsage Sum(TokenUsage? a,TokenUsage b) => a is null ? b : new(a.Input+b.Input,a.Output+b.Output,a.Total+b.Total,
        a.Reasoning is not null && b.Reasoning is not null ? a.Reasoning+b.Reasoning : null);
    private readonly HttpClient client;
    private readonly TimeSpan answerTimeout;
    public EndpointClient(HttpMessageHandler? handler = null, TimeSpan? answerTimeout = null)
    {
        this.answerTimeout = answerTimeout ?? TimeSpan.FromSeconds(75);
        client = handler is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(90);
    }
    private static HttpRequestMessage Request(string url, string key, string auth)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AppSettings.ValidateEndpoint(url));
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (auth == "api-key") request.Headers.Add("api-key", key.Trim());
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        }
        return request;
    }
    public async Task<string> TranscribeAsync(byte[] wav, AppSettings settings, string key, CancellationToken ct)
    {
        using var request = Request(settings.TranscriptionUrl, key, settings.TranscriptionAuth);
        var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "audio.wav");
        if (!string.IsNullOrWhiteSpace(settings.TranscriptionModel)) form.Add(new StringContent(settings.TranscriptionModel), "model");
        form.Add(new StringContent("json"), "response_format");
        request.Content = form;
        using var response = await client.SendAsync(request, ct);
        CheckResponse(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!json.RootElement.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Transcription response must contain a string 'text' field.");
        return text.GetString()!.Trim();
    }
    public async Task AnswerAsync(string question, AppSettings settings, string key, Action<string> append, CancellationToken ct, Action<TokenUsage>? reportUsage = null, string? systemInstructions = null, Action<string, string>? reportMetadata = null,
        IReadOnlyList<ChatTurn>? conversation = null, string continuationModel = "", Action<bool>? reportIncompleteUsage = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(answerTimeout);
        try
        {
            await ReadAnswerAsync(question, settings, key, append, deadline.Token, reportUsage, systemInstructions,
                reportMetadata, conversation, continuationModel, reportIncompleteUsage);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("The AI response timed out. The next caller question can still be answered. Check your account usage before retrying this question.");
        }
    }
    private async Task ReadAnswerAsync(string question, AppSettings settings, string key, Action<string> append, CancellationToken ct,
        Action<TokenUsage>? reportUsage, string? systemInstructions, Action<string, string>? reportMetadata,
        IReadOnlyList<ChatTurn>? conversation, string continuationModel, Action<bool>? reportIncompleteUsage)
    {
        settings.ValidateAnswer();
        if (string.IsNullOrWhiteSpace(key) && !AppSettings.ValidateEndpoint(settings.AnswerUrl).IsLoopback)
            throw new ArgumentException("Background AI connection is not configured for this endpoint.");
        using var request = Request(settings.AnswerUrl, key, settings.AnswerAuth);
        if (settings.CommercialMode) request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
        if (request.RequestUri!.AbsolutePath.StartsWith("/models/", StringComparison.OrdinalIgnoreCase))
            request.Headers.Add("extra-parameters", "pass-through");
        var payload = new Dictionary<string, object>
        {
            ["messages"] = conversation ?? new[] { new ChatTurn("system", systemInstructions ?? AnswerPresentation.ComposePrompt(settings.SystemPrompt, settings.Candidate, question)), new ChatTurn("user", question) },
            ["stream"] = true, ["stream_options"] = new { include_usage = true },
            ["max_tokens"] = settings.MaxTokens, ["temperature"] = settings.Temperature
        };
        if (!settings.CommercialMode) payload["model"] = settings.AnswerModel;
        if (settings.CommercialMode) { payload["session_id"] = settings.CommercialSessionId; payload["purpose"] = systemInstructions == CandidateProfile.AnalysisInstructions ? "resume" : "answer"; }
        if(conversation is not null) { payload["original_question"]=question; payload["continuation_model"]=continuationModel; }
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        CheckResponse(response);
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
            var received = false;
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") break;
                if (data.Length == 0) continue;
                using var json = JsonDocument.Parse(data);
                var eventRoot = json.RootElement;
                if(eventRoot.ValueKind != JsonValueKind.Object)continue;
                if(eventRoot.TryGetProperty("usage_incomplete",out var pending) && pending.ValueKind==JsonValueKind.True)reportIncompleteUsage?.Invoke(true);
                if (eventRoot.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String) reportMetadata?.Invoke(model.GetString()!, "");
                if (json.RootElement.TryGetProperty("error", out _)) throw new InvalidDataException("AI endpoint returned a streaming error.");
                if (TokenUsage.Read(json.RootElement) is { } usage) reportUsage?.Invoke(usage);
                if (!eventRoot.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 || choices[0].ValueKind != JsonValueKind.Object) continue;
                if (choices[0].TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String) reportMetadata?.Invoke("", finish.GetString()!);
                if (choices[0].TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    var token = content.GetString()!;
                    if (token.Length > 0) { append(token); received = true; }
                }
            }
            if (!received) throw new InvalidDataException("AI endpoint returned no answer text.");
        }
        else
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (TokenUsage.Read(json.RootElement) is { } usage) reportUsage?.Invoke(usage);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 || choices[0].ValueKind != JsonValueKind.Object ||
                !choices[0].TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("AI response must contain choices[0].message.content, or stream chat completion events.");
            if (json.RootElement.TryGetProperty("model", out var resolved) && resolved.ValueKind == JsonValueKind.String) reportMetadata?.Invoke(resolved.GetString()!, "");
            if (choices[0].TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String) reportMetadata?.Invoke("", reason.GetString()!);
            append(content.GetString()!);
        }
    }
    private static void CheckResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Endpoint returned HTTP {(int)response.StatusCode}. Check URL, model, credentials, and provider availability.", null, response.StatusCode);
    }
    public void Dispose() => client.Dispose();
}

