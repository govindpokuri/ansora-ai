using System;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading.Tasks;

namespace OverlayCopilot.Services;

public static class CallExporter
{
    public static async Task WriteAtomicAsync(string path, string content)
    {
        if (Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase) && !content.EndsWith("</html>"))
            throw new InvalidDataException("Incomplete HTML export.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false));
            using (var file = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.None)) file.Flush(true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string Json(CallReport report) => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    private static string Escape(string value) => WebUtility.HtmlEncode(value);
    private static string Time(DateTimeOffset? time) => time?.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) ?? "In progress";
    public static string Html(CallReport report)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Call reference report</title><style>
            body{font-family:Segoe UI,Arial,sans-serif;color:#17243a;background:#eef2f8;margin:0;line-height:1.6}
            main{max-width:1000px;margin:32px auto;padding:36px;background:white;border-radius:12px}
            h1{font-size:30px;margin:0}h2{margin-top:32px;font-size:21px}h3{font-size:18px;margin:0 0 12px}
            .muted{color:#536781;font-size:13px}.metrics{padding:18px;background:#edf7f4;border-radius:8px;margin:24px 0}
            article{border:1px solid #dce4ef;padding:22px;margin:16px 0;border-radius:8px;break-inside:avoid}
            .question{font-weight:600;font-size:18px}.usage{background:#f3f6fc;padding:10px;margin-top:16px;font-size:13px}
            pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f3f6fc;padding:14px}code{font-family:Consolas,monospace}
            p{margin:12px 0}details{margin-top:12px}summary{cursor:pointer}li{margin:6px 0}
            @media print{body{background:white}main{margin:0;padding:0;max-width:none}article{break-inside:auto}details{display:none}}
            </style></head><body><main><h1>Call reference report</h1>
            """);
        html.Append($"<p class='muted'>Call ID: {report.Id}<br>Exported: {Time(report.ExportedAt)}</p>");
        var usage = report.ReportedUsage;
        html.Append($"<section class='metrics'><strong>Reported AI tokens: {usage.Total:N0}</strong><br>Input: {usage.Input:N0} · Output: {usage.Output:N0} · AI requests: {report.Answers.Length}<br>");
        html.Append($"Usage unavailable for {report.MissingUsageRequests} request(s). Counts are provider-reported totals, not estimates. Output may include reasoning tokens. Connection checks are excluded.</section>");
        html.Append("<p class='muted'>Includes all caller text recognized by this app and every AI response since the last Clear. Your microphone and raw call audio are not recorded. Recognition timestamps indicate when text was first recognized; partial text may remain when listening stops.</p>");
        html.Append("<h2>Capture timeline</h2>");
        foreach (var period in report.CapturePeriods)
            html.Append($"<p>{Time(period.StartedAt)} → {Time(period.EndedAt)}<br><span class='muted'>Audio device: {Escape(period.Device)}</span></p>");
        html.Append("<h2>Questions &amp; AI answers</h2>");
        if (report.Answers.Length == 0) html.Append("<p>No AI answers were requested.</p>");
        for (var i = 0; i < report.Answers.Length; i++)
        {
            var answer = report.Answers[i];
            html.Append($"<article><h3>Question {i + 1}</h3><p class='question'>{Escape(answer.Question)}</p><p class='muted'>{Time(answer.StartedAt)} → {Time(answer.CompletedAt)} · {Escape(answer.Status)} · Model: {Escape(answer.Model)}</p>");
            html.Append($"<p class='muted'>Question ID: {Escape(answer.QuestionId)} · Prompt: {Escape(answer.PromptVersion)} · First text: {Time(answer.FirstTokenAt)} · Finish: {Escape(answer.FinishReason)}</p>");
            html.Append(Markdown(answer.Answer.Length > 0 ? answer.Answer : "No answer text was returned."));
            html.Append(answer.Usage is { } tokens
                ? $"<div class='usage'>Input: {tokens.Input:N0} · Output: {tokens.Output:N0} · Total: {tokens.Total:N0}" + (tokens.Reasoning is { } reasoning ? $" · Reasoning (included in output): {reasoning:N0}" : "") + "</div>"
                : "<div class='usage'>Token usage was not returned for this request; it is not counted as zero.</div>");
            if(answer.UsageIncomplete)html.Append("<p class='muted'>Usage is incomplete: additional provider usage may still be pending.</p>");
            html.Append($"<details><summary>AI request context &amp; instructions</summary><p>Max tokens: {answer.MaxTokens} · Temperature: {answer.Temperature.ToString(CultureInfo.InvariantCulture)}</p><h4>Context sent</h4><pre>{Escape(answer.Prompt)}</pre><h4>Instructions</h4><pre>{Escape(answer.Instructions)}</pre></details></article>");
        }
        html.Append("<h2>Complete caller transcript</h2>");
        if (report.Transcript.Length == 0) html.Append("<p>No caller words were recognized.</p>");
        foreach (var turn in report.Transcript)
            html.Append($"<article><p class='muted'>Caller · {Time(turn.RecognizedAt)}{(turn.Final ? "" : " · Partial wording")}</p><p>{Escape(turn.Text)}</p></article>");
        return html.Append("</main></body></html>").ToString();
    }
    private static string Markdown(string text)
    {
        var html = new StringBuilder(); var inCode = false; var inList = false;
        void CloseList() { if (inList) { html.Append("</ul>"); inList = false; } }
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                CloseList(); html.Append(inCode ? "</code></pre>" : "<pre><code>"); inCode = !inCode; continue;
            }
            if (inCode) { html.Append(Escape(line)).Append('\n'); continue; }
            if (string.IsNullOrWhiteSpace(line)) { CloseList(); continue; }
            var heading = Regex.Match(line, @"^#{1,6}\s+(.+)$");
            var bullet = Regex.Match(line, @"^\s*(?:[-*+]\s+|\d+[.)]\s+)(.+)$");
            if (heading.Success) { CloseList(); html.Append("<h3>").Append(Inline(heading.Groups[1].Value)).Append("</h3>"); }
            else if (bullet.Success)
            {
                if (!inList) { html.Append("<ul>"); inList = true; }
                html.Append("<li>").Append(Inline(bullet.Groups[1].Value)).Append("</li>");
            }
            else { CloseList(); html.Append("<p>").Append(Inline(line)).Append("</p>"); }
        }
        CloseList(); if (inCode) html.Append("</code></pre>"); return html.ToString();
    }
    private static string Inline(string text)
    {
        var safe = Escape(text);
        safe = Regex.Replace(safe, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
        return Regex.Replace(safe, @"`([^`]+)`", "<code>$1</code>");
    }
}
