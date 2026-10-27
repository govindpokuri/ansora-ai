using System;
using System.Collections.Generic;
using System.Linq;

namespace OverlayCopilot.Services;

// A draft replaces its own turn; revisions never become duplicate conversation lines.
public sealed class LiveTranscript
{
    private readonly bool callerOnly;
    public LiveTranscript(bool callerOnly = false) => this.callerOnly = callerOnly;
    private sealed record Turn(int Session, string Source, long Segment, string Text, bool Final);
    private readonly List<Turn> turns = new();
    public void Update(int session, string source, long segment, string text, bool final)
    {
        if (callerOnly && source != "Call" && source != "Caller") return;
        if (callerOnly) source = "Caller";
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 12000) text = text[^12000..];
        var index = turns.FindIndex(t => t.Session == session && t.Source == source && t.Segment == segment);
        var turn = new Turn(session, source, segment, text.Trim(), final);
        if (index < 0) turns.Add(turn); else turns[index] = turn;
        while (turns.Count > 1 && turns.Sum(t => t.Text.Length) > 12000) turns.RemoveAt(0);
    }
    public string Display => string.Join("\n\n", turns.Select(t => $"{t.Source}{(t.Final ? "" : " (live)")} · {t.Text}"));
    public string Context
    {
        get
        {
            var text = string.Join("\n", turns.Select(t => $"{t.Source}: {t.Text}"));
            return text.Length > 6000 ? text[^6000..] : text;
        }
    }
    public void Clear() => turns.Clear();
}
