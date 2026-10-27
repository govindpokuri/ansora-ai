using System.Text.Json;

namespace OverlayCopilot.Services;

public sealed record TokenUsage(long Input, long Output, long Total, long? Reasoning = null)
{
    public static TokenUsage? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;
        static long? Count(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number
            && field.TryGetInt64(out var count) && count >= 0 ? count : null;
        var input = Count(usage, "prompt_tokens") ?? Count(usage, "input_tokens");
        var output = Count(usage, "completion_tokens") ?? Count(usage, "output_tokens");
        if (input is null || output is null) return null;
        var total = Count(usage, "total_tokens") ?? input.Value + output.Value;
        long? reasoning = null;
        if (usage.TryGetProperty("completion_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object)
            reasoning = Count(details, "reasoning_tokens");
        return new(input.Value, output.Value, total, reasoning);
    }
}
