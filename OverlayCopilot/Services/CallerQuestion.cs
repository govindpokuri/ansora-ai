using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace OverlayCopilot.Services;

public static class CallerQuestion
{
    private static readonly Regex question = new(@"\b(what|why|how|when|where|who|which|explain|describe|define|compare|implement|write|solve|introduce)\b|\b(can|could|would|will|have|do|did) you\b|\btell me\b|\bwalk me\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static bool IsReady(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || (!text.Contains('?') && !question.IsMatch(text))) return false;
        if (text.TrimEnd().EndsWith("...") || Regex.IsMatch(text, @"(?i)difference between\s+(\w+)\s+and\s+\1[?.!]*$")) return false;
        var last = words[^1].Trim('.', '?', ',', '!', ':').ToLowerInvariant();
        return !new[] { "is", "are", "the", "a", "an", "to", "of", "and", "or", "from", "every", "get", "can", "you", "me", "between", "what", "how", "explain", "describe", "define", "write", "compare" }.Contains(last);
    }
}
