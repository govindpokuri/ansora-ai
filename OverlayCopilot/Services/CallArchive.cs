using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverlayCopilot.Services;

public sealed record CallerTurn(int Session, long Segment, DateTimeOffset RecognizedAt, string Text, bool Final);
public sealed record CapturePeriod(DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string Device);
public sealed record RecordedAnswer(DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string Question,
    string Prompt, string Model, int MaxTokens, double Temperature, string Instructions, string Answer, string Status, TokenUsage? Usage,
    string QuestionId = "", DateTimeOffset? FirstTokenAt = null, string FinishReason = "", string PromptVersion = "compact-v1", bool UsageIncomplete = false);
public sealed record CallReport(Guid Id, DateTimeOffset ExportedAt, CapturePeriod[] CapturePeriods,
    CallerTurn[] Transcript, RecordedAnswer[] Answers, TokenUsage ReportedUsage, int MissingUsageRequests);

public sealed class CallAnswer
{
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; private set; }
    public string Question { get; }
    public string Prompt { get; }
    private readonly AppSettings settings;
    private readonly StringBuilder text = new();
    private readonly object gate = new();
    private long revision;
    public long Revision => System.Threading.Interlocked.Read(ref revision);
    private readonly string instructions;
    public string QuestionId { get; }
    public string ResolvedModel { get; set; } = "";
    public string FinishReason { get; set; } = "";
    public DateTimeOffset? FirstTokenAt { get; private set; }
    public string Status { get; private set; } = "Streaming";
    private TokenUsage? usage;
    public TokenUsage? Usage { get => usage; set { usage = value; System.Threading.Interlocked.Increment(ref revision); } }
    private bool usageIncomplete;
    public bool UsageIncomplete { get => usageIncomplete; set { usageIncomplete = value; System.Threading.Interlocked.Increment(ref revision); } }
    public CallAnswer(string question, string prompt, AppSettings settings, string questionId = "") { Question = question; Prompt = prompt; this.settings = settings; QuestionId = questionId; instructions = AnswerPresentation.ComposePrompt(settings.SystemPrompt, settings.Candidate, prompt); }
    public void Append(string token) { lock(gate) { if (token.Length > 0) FirstTokenAt ??= DateTimeOffset.Now; text.Append(token); System.Threading.Interlocked.Increment(ref revision); } }
    public void Finish(string status) { lock(gate) { Status = status; CompletedAt = DateTimeOffset.Now; System.Threading.Interlocked.Increment(ref revision); } }
    public RecordedAnswer Snapshot() { lock(gate) return new(StartedAt, CompletedAt, Question, Prompt,
        ResolvedModel.Length > 0 ? ResolvedModel : settings.CommercialMode ? "Backend-managed (model not returned)" : settings.AnswerModel,
        settings.MaxTokens, settings.Temperature, instructions, text.ToString(), Status, Usage, QuestionId, FirstTokenAt, FinishReason, UsageIncomplete: UsageIncomplete); }
}

// Unlike the rolling LLM context, this archive retains the entire call until Clear or exit.
public sealed class CallArchive
{
    private readonly Guid id = Guid.NewGuid();
    private long revision;
    public Guid Id => id;
    public long Revision => revision + answers.Sum(a => a.Revision);
    private readonly List<CallerTurn> turns = new();
    private readonly Dictionary<(int, long), int> turnIndices = new();
    private readonly List<CallAnswer> answers = new();
    private readonly List<CapturePeriod> periods = new();
    public bool HasContent => turns.Count > 0 || answers.Count > 0;
    public int RequestCount => answers.Count;
    public int MissingUsageRequests => answers.Count(a => a.CompletedAt is not null && (a.Usage is null || a.UsageIncomplete));
    public int PendingRequests => answers.Count(a => a.CompletedAt is null);
    public TokenUsage ReportedUsage => new(answers.Sum(a => a.Usage?.Input ?? 0), answers.Sum(a => a.Usage?.Output ?? 0), answers.Sum(a => a.Usage?.Total ?? 0));
    public void StartCapture(string device)
    {
        EndCapture(); periods.Add(new(DateTimeOffset.Now, null, device)); revision++;
    }
    public void EndCapture()
    {
        if (periods.Count > 0 && periods[^1].EndedAt is null) { periods[^1] = periods[^1] with { EndedAt = DateTimeOffset.Now }; revision++; }
    }
    public void UpdateCaller(int session, long segment, string text, bool final)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        revision++;
        var key = (session, segment);
        if (turnIndices.TryGetValue(key, out var index))
            turns[index] = turns[index] with { Text = text.Trim(), Final = final };
        else { turnIndices[key] = turns.Count; turns.Add(new(session, segment, DateTimeOffset.Now, text.Trim(), final)); }
    }
    public CallAnswer StartAnswer(string question, string prompt, AppSettings settings, string questionId = "")
    {
        var answer = new CallAnswer(question, prompt, settings, questionId); answers.Add(answer); revision++; return answer;
    }
    public CallReport Snapshot() => new(id, DateTimeOffset.Now, periods.ToArray(), turns.ToArray(),
        answers.Select(a => a.Snapshot()).ToArray(), ReportedUsage, MissingUsageRequests);
}
