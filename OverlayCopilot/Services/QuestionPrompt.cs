using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OverlayCopilot.Services;
public static class QuestionPrompt
{
    public static string Build(string question, string context)
    {
        var current = question.Trim();
        var recent = context.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.StartsWith("Caller: ") ? t[8..].Trim() : t.Trim())
            .Where(t => t != current && t.Length > 12 && !Regex.IsMatch(t, @"(?i)^\(?gentle music|^(hi|hello|thank you|goodnight|mm-hmm)\b"))
            .TakeLast(3).Select(t => t.Length > 450 ? t[..450] : t).ToArray();
        return "Answer CURRENT QUESTION. Recent caller context is only for resolving references.\n"
            + JsonSerializer.Serialize(new { currentQuestion = current, recentCallerContext = recent });
    }
}
