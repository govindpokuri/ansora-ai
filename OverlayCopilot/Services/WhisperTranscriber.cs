using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace OverlayCopilot.Services;

public sealed class WhisperTranscriber : IDisposable
{
    private readonly WhisperFactory factory;
    private readonly WhisperProcessor processor;
    private readonly SemaphoreSlim gate = new(1);
    public static string ModelPath
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "models", "ggml-base.en.bin");
                if (File.Exists(path)) return path;
            }
            throw new InvalidOperationException("Whisper model missing. Put ggml-base.en.bin in the models folder beside this app or in the project folder.");
        }
    }
    public WhisperTranscriber()
    {
        factory = WhisperFactory.FromPath(ModelPath);
        try
        {
            processor = factory.CreateBuilder().WithLanguage("en").WithNoContext()
                .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 6)).Build();
        }
        catch { factory.Dispose(); throw; }
    }
    public static async Task<WhisperTranscriber> CreateReadyAsync(CancellationToken ct)
    {
        var speech = await Task.Run(() => new WhisperTranscriber(), ct).ConfigureAwait(false);
        try
        {
            // Compile GPU kernels before capturing speech so the first phrase is not delayed.
            await speech.TranscribeAsync(AudioSegmenter.ToWav(new byte[96000]), ct).ConfigureAwait(false);
            return speech;
        }
        catch { await Task.Run(speech.Dispose).ConfigureAwait(false); throw; }
    }
    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var stream = new MemoryStream(wav, false);
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(stream, ct).ConfigureAwait(false))
            {
                var value = segment.Text.Trim();
                if (value.StartsWith('[') && value.EndsWith(']')) continue;
                if (text.Length > 0) text.Append(' ');
                text.Append(value);
            }
            return text.ToString().Trim();
        }
        finally { gate.Release(); }
    }
    public void Dispose()
    {
        // Cancellation can end enumeration before native inference has unwound.
        processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        factory.Dispose(); gate.Dispose();
    }
}
