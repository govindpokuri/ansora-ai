using System;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OverlayCopilot.Services;

public sealed class LocalSpeechTranscriber : IDisposable
{
    private readonly SpeechRecognitionEngine engine;
    public LocalSpeechTranscriber()
    {
        var installed = SpeechRecognitionEngine.InstalledRecognizers();
        var recognizer = installed.FirstOrDefault(r => r.Culture.Name == "en-US") ?? installed.FirstOrDefault();
        if (recognizer is null) throw new InvalidOperationException("No Windows desktop speech recognizer is installed. Configure a transcription endpoint, or install a Windows speech language pack.");
        try { engine = new SpeechRecognitionEngine(recognizer); }
        catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException("Windows speech recognition is unavailable for this user. Use a transcription endpoint or install/configure a desktop speech recognizer.", ex);
        }
        engine.LoadGrammar(new DictationGrammar());
        engine.InitialSilenceTimeout = TimeSpan.FromSeconds(2);
        engine.BabbleTimeout = TimeSpan.FromSeconds(2);
    }
    public Task<string> TranscribeAsync(byte[] wav, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(wav, false);
        engine.SetInputToWaveStream(stream);
        var text = new StringBuilder();
        try
        {
            // The worker processes one bounded chunk at a time; recognition finishes at WAV EOF.
            for (var i = 0; i < 12; i++)
            {
                ct.ThrowIfCancellationRequested();
                var result = engine.Recognize(TimeSpan.FromSeconds(5));
                if (result is null) break;
                if (text.Length > 0) text.Append(' ');
                text.Append(result.Text);
            }
            ct.ThrowIfCancellationRequested();
            return text.ToString();
        }
        finally { engine.SetInputToNull(); }
    }, ct);
    public void Dispose() => engine.Dispose();
}
