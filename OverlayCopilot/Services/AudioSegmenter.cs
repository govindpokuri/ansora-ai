using System;
using System.Collections.Generic;
using System.IO;

namespace OverlayCopilot.Services;

public sealed record AudioChunk(byte[] Pcm, bool EndOfUtterance, bool Preview = false, long SegmentId = 0);

// PCM16, mono, 16 kHz. Fixed 20-ms analysis frames keep segmentation independent of callback size.
public sealed class AudioSegmenter
{
    private const int FrameBytes = 640;
    private readonly List<byte> remainder = new();
    private readonly MemoryStream buffer = new();
    private bool utterance;
    private int silentFrames;
    private DateTimeOffset lastInput;
    private long segmentId;
    private int nextPreview = 32000 * 2;
    private readonly Queue<byte[]> preRoll = new();
    public IReadOnlyList<AudioChunk> Feed(byte[] data, int count, DateTimeOffset now)
    {
        var output = new List<AudioChunk>();
        // NAudio emits callbacks with zero bytes when no output packets are available.
        // Those callbacks must not reset the idle deadline of the last real audio.
        if (count == 0) return output;
        lastInput = now;
        for (var i = 0; i < count; i++) remainder.Add(data[i]);
        var offset = 0;
        while (remainder.Count - offset >= FrameBytes)
        {
            var frame = remainder.GetRange(offset, FrameBytes).ToArray();
            offset += FrameBytes;
            double sum = 0;
            for (var j = 0; j < frame.Length; j += 2)
            {
                var sample = (short)(frame[j] | frame[j + 1] << 8) / 32768.0;
                sum += sample * sample;
            }
            var speech = Math.Sqrt(sum / (FrameBytes / 2)) >= 0.004;
            if (speech)
            {
                if (!utterance) { segmentId++; foreach (var preceding in preRoll) buffer.Write(preceding); preRoll.Clear(); }
                utterance = true; silentFrames = 0;
            }
            else silentFrames++;
            if (!utterance) { preRoll.Enqueue(frame); if (preRoll.Count > 15) preRoll.Dequeue(); continue; }
            buffer.Write(frame);
            if (silentFrames >= 50) Finish(output);
            else if (buffer.Length >= 32000 * 15)
            {
                output.Add(new(buffer.ToArray(), false, false, segmentId));
                buffer.SetLength(0); buffer.Position = 0; segmentId++; nextPreview = 32000 * 2;
            }
            else if (buffer.Length >= nextPreview)
            {
                output.Add(new(buffer.ToArray(), false, true, segmentId));
                nextPreview += 32000 * 2;
            }
        }
        if (offset > 0) remainder.RemoveRange(0, offset);
        return output;
    }
    // Loopback callbacks can stop entirely when the output device goes silent.
    public IReadOnlyList<AudioChunk> Tick(DateTimeOffset now)
    {
        var output = new List<AudioChunk>();
        if (utterance && now - lastInput >= TimeSpan.FromMilliseconds(1100)) Finish(output);
        return output;
    }
    private void Finish(List<AudioChunk> output)
    {
        output.Add(new(buffer.ToArray(), true, false, segmentId));
        buffer.SetLength(0); buffer.Position = 0;
        utterance = false;
        silentFrames = 0;
        nextPreview = 32000 * 2;
    }
    public static byte[] ToWav(byte[] pcm)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
        return stream.ToArray();
    }
}


