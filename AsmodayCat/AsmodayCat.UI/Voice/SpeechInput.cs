using AsmodayCat.UI.Localization;
using System.IO;
using NAudio.Wave;
using Whisper.net;

namespace AsmodayCat.UI.Voice;

// Microphone → text. Records until the speaker pauses (or Stop is pressed), then runs Whisper.
public sealed class SpeechInput : IDisposable
{
    private const int SampleRate = 16000;                       // what Whisper expects

    private static readonly TimeSpan SilenceToStop  = TimeSpan.FromSeconds(1.3);
    private static readonly TimeSpan NoSpeechLimit  = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MaxRecording   = TimeSpan.FromSeconds(45);

    private WhisperFactory?   _factory;
    private WhisperProcessor? _processor;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private TaskCompletionSource<bool>? _stopRequested;
    private string? _processorLang;

    public static bool HasMicrophone => WaveInEvent.DeviceCount > 0;

    // Ends the current recording early (the recorded part is still recognized)
    public void Stop() => _stopRequested?.TrySetResult(true);

    // Records one phrase and returns its text ("" if nothing was said).
    public async Task<string> ListenAsync(Action<string>? onState, CancellationToken ct)
    {
        if (!HasMicrophone) throw new InvalidOperationException("No microphone found.");

        onState?.Invoke(Loc.T("voice.listening"));
        var samples = await RecordPhraseAsync(ct);
        if (samples.Length < SampleRate / 2) return string.Empty;   // < 0.5 s

        onState?.Invoke(Loc.T("voice.recognizing"));
        await EnsureLoadedAsync(ct);

        var text = new System.Text.StringBuilder();
        await foreach (var segment in _processor!.ProcessAsync(samples, ct))
            text.Append(segment.Text);

        return CleanUp(text.ToString());
    }

    private async Task<float[]> RecordPhraseAsync(CancellationToken ct)
    {
        _stopRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var done    = _stopRequested;
        var samples = new List<float>(SampleRate * 10);
        var started = DateTime.UtcNow;
        DateTime? lastVoice = null;
        var noiseFloor = 0.01f;

        using var wave = new WaveInEvent
        {
            WaveFormat         = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 100
        };

        wave.DataAvailable += (_, e) =>
        {
            float sumSq = 0;
            var count = e.BytesRecorded / 2;
            lock (samples)
            {
                for (var i = 0; i < count; i++)
                {
                    var s = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                    samples.Add(s);
                    sumSq += s * s;
                }
            }

            // Simple voice activity detection: loudness well above the room's noise floor
            var rms = MathF.Sqrt(sumSq / Math.Max(count, 1));
            var now = DateTime.UtcNow;
            if (lastVoice is null && now - started < TimeSpan.FromMilliseconds(400))
                noiseFloor = Math.Max(noiseFloor, rms);                 // first 0.4 s: measure the room
            else if (rms > Math.Max(noiseFloor * 2.5f, 0.02f))
                lastVoice = now;

            if (lastVoice is { } v && now - v > SilenceToStop) done.TrySetResult(true);
            if (lastVoice is null && now - started > NoSpeechLimit) done.TrySetResult(true);
            if (now - started > MaxRecording) done.TrySetResult(true);
        };

        wave.StartRecording();
        try
        {
            await done.Task.WaitAsync(ct);
        }
        finally
        {
            wave.StopRecording();
            _stopRequested = null;
        }

        if (lastVoice is null) return [];
        lock (samples) return samples.ToArray();
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_processor is not null && _processorLang == Loc.Instance.Lang) return;
        await _loadGate.WaitAsync(ct);
        try
        {
            if (_processor is not null && _processorLang == Loc.Instance.Lang) return;
            // Language switched: rebuild the recognizer for the new one
            _processor?.Dispose();
            _factory?.Dispose();
            _processor = null;
            _factory   = null;
            var lang = Loc.Instance.Lang;
            await VoiceAssets.EnsureWhisperAsync(null, ct);
            await Task.Run(() =>
            {
                _factory   = WhisperFactory.FromPath(VoiceAssets.WhisperModel);
                _processor = _factory.CreateBuilder()
                    .WithLanguage(lang)
                    .WithThreads(Math.Max(2, Environment.ProcessorCount - 2))
                    .Build();
            }, ct);
            _processorLang = lang;
        }
        finally { _loadGate.Release(); }
    }

    // Whisper marks non-speech like "[музика]" or "(сміється)"; drop those
    private static string CleanUp(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)", " ");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
