using System.Collections.Concurrent;
using System.Text;

namespace OverlayCopilot.Services;

// Producer runs away from the dispatcher; the UI consumes a batch on its own timer.
public sealed class AnswerStreamBuffer
{
    private readonly ConcurrentQueue<string> tokens = new();
    public string Question { get; }
    public int Generation { get; }
    public bool FirstBatch { get; set; } = true;
    public AnswerStreamBuffer(string question, int generation) { Question = question; Generation = generation; }
    public void Append(string text) { if (text.Length > 0) tokens.Enqueue(text); }
    public string Drain()
    {
        var batch = new StringBuilder();
        while (tokens.TryDequeue(out var token)) batch.Append(token);
        return batch.ToString();
    }
}
