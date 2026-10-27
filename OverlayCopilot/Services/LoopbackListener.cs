using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OverlayCopilot.Services;

public sealed record AudioDevice(string Id, string Name);
public sealed class LoopbackListener : IAsyncDisposable
{
    private readonly MMDevice device;
    private readonly WasapiCapture capture;
    private readonly AudioSegmenter segmenter = new();
    private readonly object gate = new();
    private readonly Channel<AudioChunk> chunks = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly Timer timer;
    private bool stopped;
    private bool started;
    private long receivedBytes;
    private float peak;
    private readonly TaskCompletionSource recordingStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ChannelReader<AudioChunk> Reader => chunks.Reader;
    public event Action<string>? Error;
    public string DeviceName => device.FriendlyName;
    public long ReceivedBytes => Interlocked.Read(ref receivedBytes);
    public float Peak => Volatile.Read(ref peak);

    public static IReadOnlyList<AudioDevice> GetDevices(bool microphone = false)
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioDevice>();
        foreach (var item in enumerator.EnumerateAudioEndPoints(microphone ? DataFlow.Capture : DataFlow.Render, DeviceState.Active))
        { using (item) result.Add(new(item.ID, item.FriendlyName)); }
        return result;
    }
    public static string GetDefaultDeviceId(bool microphone = false)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(microphone ? DataFlow.Capture : DataFlow.Render, Role.Multimedia);
        return device.ID;
    }
    public LoopbackListener(string deviceId, bool microphone = false)
    {
        using var enumerator = new MMDeviceEnumerator();
        device = string.IsNullOrEmpty(deviceId) ? enumerator.GetDefaultAudioEndpoint(microphone ? DataFlow.Capture : DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(deviceId);
        capture = microphone ? new WasapiCapture(device) : new WasapiLoopbackCapture(device);
        capture.WaveFormat = new WaveFormat(16000, 16, 1);
        capture.DataAvailable += (_, e) =>
        {
            lock (gate)
            {
                if (stopped || e.BytesRecorded == 0) return;
                Interlocked.Add(ref receivedBytes, e.BytesRecorded);
                float currentPeak = 0;
                for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
                    currentPeak = Math.Max(currentPeak, Math.Abs((short)(e.Buffer[i] | e.Buffer[i + 1] << 8) / 32768f));
                Volatile.Write(ref peak, currentPeak);
                Publish(segmenter.Feed(e.Buffer, e.BytesRecorded, DateTimeOffset.UtcNow));
            }
        };
        capture.RecordingStopped += (_, e) =>
        {
            lock (gate)
            {
                stopped = true;
                chunks.Writer.TryComplete(e.Exception);
            }
            recordingStopped.TrySetResult();
            if (e.Exception is not null) Error?.Invoke($"Audio capture stopped (0x{e.Exception.HResult:X8}). Re-select the audio device and restart listening.");
        };
        timer = new Timer(_ => { lock (gate) { if (!stopped) { Volatile.Write(ref peak, peak * 0.65f); Publish(segmenter.Tick(DateTimeOffset.UtcNow)); } } }, null, Timeout.Infinite, Timeout.Infinite);
    }
    public void Start()
    {
        capture.StartRecording();
        started = true;
        timer.Change(200, 200);
    }
    private void Publish(IReadOnlyList<AudioChunk> items)
    {
        foreach (var item in items)
            if (!chunks.Writer.TryWrite(item))
            {
                if (item.Preview) continue; // Drafts may be skipped; committed speech must never be silently lost.
                stopped = true;
                chunks.Writer.TryComplete(new InvalidOperationException("Transcription cannot keep up. Listening stopped; restart after checking endpoint speed."));
                Error?.Invoke("Transcription queue full. Stop and restart listening after checking endpoint speed.");
                break;
            }
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate) { stopped = true; chunks.Writer.TryComplete(); }
        await timer.DisposeAsync();
        try
        {
            if (started && capture.CaptureState != CaptureState.Stopped)
            {
                capture.StopRecording();
                await recordingStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally { capture.Dispose(); device.Dispose(); }
    }
}
