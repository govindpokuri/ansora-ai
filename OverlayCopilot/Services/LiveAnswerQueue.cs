using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OverlayCopilot.Services;
public sealed record AnswerJob(long Version, string Prompt, string Question = "", string QuestionId = "");

// Speech keeps updating independently; changing drafts do not become separate paid questions.
public sealed class LiveAnswerQueue
{
    private sealed record Pending(string Context, string Question, string Id, bool Final, DateTimeOffset Changed);
    private Pending? pending;
    private readonly Dictionary<string, string> submitted = new();
    private readonly HashSet<string> corrected = new();
    private string lastQuestion = "";
    private DateTimeOffset lastStart = DateTimeOffset.MinValue;
    private bool running;
    private long version;
    public int ConsolidatedRevisions { get; private set; }
    public void Offer(string context, string question = "", string turnId = "", bool final = true, DateTimeOffset? observedAt = null)
    {
        var now = observedAt ?? DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(question)) question = context.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Replace("Caller: ", "") ?? "";
        question = question.Trim();
        var normalized = Normalize(question);
        var id = turnId.Length > 0 ? turnId : normalized;
        if (!CallerQuestion.IsReady(question)) { if (pending?.Id == id) pending = null; return; }
        if (submitted.TryGetValue(id, out var previous))
        {
            // One final correction can add real constraints or change polarity/numbers.
            if (!final || corrected.Contains(id) || !MaterialChange(previous, normalized)) { ConsolidatedRevisions++; return; }
        }
        else if (normalized == lastQuestion && now - lastStart < TimeSpan.FromSeconds(25)) { ConsolidatedRevisions++; if (pending?.Id == id) pending = null; return; }
        if (pending?.Id == id)
        {
            ConsolidatedRevisions++;
            var changed = Normalize(pending.Question) == normalized ? pending.Changed : now;
            pending = new(context, question, id, final, changed);
        }
        else pending = new(context, question, id, final, now);
    }
    public AnswerJob? TryTake(DateTimeOffset now)
    {
        if (running || pending is null || now - lastStart < TimeSpan.FromSeconds(1)) return null;
        // Audio previews arrive every two seconds. A longer stability delay can
        // keep moving forever while a caller continues speaking.
        if (now - pending.Changed < TimeSpan.FromMilliseconds(pending.Final ? 400 : 1200)) return null;
        var item = pending; pending = null;
        if (submitted.ContainsKey(item.Id)) corrected.Add(item.Id);
        lastQuestion = Normalize(item.Question); lastStart = now; submitted[item.Id] = lastQuestion; running = true;
        if (submitted.Count > 128) { var oldest = submitted.Keys.First(); submitted.Remove(oldest); corrected.Remove(oldest); }
        return new(++version, QuestionPrompt.Build(item.Question, item.Context), item.Question, item.Id);
    }
    public void Complete(long jobVersion) { if (jobVersion == version) running = false; }
    public void Reset()
    {
        version++; running = false; pending = null; submitted.Clear(); corrected.Clear(); lastQuestion = ""; lastStart = DateTimeOffset.MinValue; ConsolidatedRevisions = 0;
    }
    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9#]+", " ").Trim();
    private static bool MaterialChange(string previous, string current)
    {
        if (previous == current) return false;
        var before = previous.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var after = current.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (new[] { "not", "cannot", "without", "only" }.Any(t => before.Contains(t) != after.Contains(t))) return true;
        if (!before.Where(t => t.All(char.IsDigit)).OrderBy(t => t).SequenceEqual(after.Where(t => t.All(char.IsDigit)).OrderBy(t => t))) return true;
        var added = after.Except(before).Count();
        var removed = before.Except(after).Count();
        return added >= 6 || (added >= 1 && removed >= 1 && added + removed >= Math.Max(2, before.Count / 2));
    }
}
