using AsmodayCat.UI.Localization;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace AsmodayCat.UI.Voice;

// Text → Ukrainian speech with Piper (offline), played through the default speakers.
public sealed class SpeechOutput : IDisposable
{
    private const int MaxSpokenChars = 1200;     // long answers: read the start, the rest stays on screen
    private const int Speaker        = 0;        // ukrainian_tts voices: 0 lada, 1 mykyta, 2 tetiana

    private WaveOutEvent?   _player;
    private CancellationTokenSource? _cts;

    public bool IsSpeaking => _player?.PlaybackState == PlaybackState.Playing;

    public void Stop()
    {
        _cts?.Cancel();
        _player?.Stop();
    }

    public async Task SpeakAsync(string markdown, CancellationToken ct = default)
    {
        Stop();
        var text = ToSpeech(markdown);
        if (text.Length == 0) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        await VoiceAssets.EnsurePiperAsync(null, token);

        Directory.CreateDirectory(VoiceAssets.TempDir);
        var wav = Path.Combine(VoiceAssets.TempDir, $"tts_{Guid.NewGuid():N}.wav");
        try
        {
            await SynthesizeAsync(text, wav, token);
            await PlayAsync(wav, token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { File.Delete(wav); } catch (IOException) { }
        }
    }

    private static async Task SynthesizeAsync(string text, string wavPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(VoiceAssets.PiperExe)
        {
            ArgumentList           = { "--model", VoiceAssets.VoiceModel, "--output_file", wavPath },
            WorkingDirectory       = Path.GetDirectoryName(VoiceAssets.PiperExe)!,
            RedirectStandardInput  = true,
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
            StandardInputEncoding  = new UTF8Encoding(false),
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        if (VoiceAssets.VoiceHasSpeakers) { psi.ArgumentList.Add("--speaker"); psi.ArgumentList.Add(Speaker.ToString()); }

        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync(ct);
        _ = process.StandardOutput.ReadToEndAsync(ct);

        // One sentence per line keeps Piper's pauses natural
        await process.StandardInput.WriteAsync(text);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw;
        }

        if (process.ExitCode != 0 || !File.Exists(wavPath))
            throw new InvalidOperationException($"Voice engine failed: {(await stderr).Trim()}");
    }

    private async Task PlayAsync(string wavPath, CancellationToken ct)
    {
        using var reader = new WaveFileReader(wavPath);
        using var player = new WaveOutEvent();
        _player = player;

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackStopped += (_, _) => finished.TrySetResult();
        player.Init(reader);
        player.Play();

        await using (ct.Register(() => player.Stop()))
            await finished.Task;

        _player = null;
    }

    // Markdown → plain sentences worth hearing: no code, no URLs, no formatting marks.
    public static string ToSpeech(string markdown)
    {
        var s = Regex.Replace(markdown, @"```.*?```", " " + Loc.T("voice.codeOnScreen") + " ", RegexOptions.Singleline);
        s = Regex.Replace(s, @"`([^`]*)`", "$1");
        s = Regex.Replace(s, @"!\[[^\]]*\]\([^)]*\)", " ");
        s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");
        s = Regex.Replace(s, @"https?://\S+", " ");
        s = Regex.Replace(s, @"^\s*#{1,6}\s*", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"^\s*([-*+]|\d+[.)])\s+", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"[*_~|>#]+", " ");
        s = Regex.Replace(s, @"_\([^)]*\)_", " ");
        s = Regex.Replace(s, @"[ \t]+", " ");
        s = Regex.Replace(s, @"\n{2,}", "\n").Trim();

        if (s.Length > MaxSpokenChars)
        {
            var cut = s.LastIndexOfAny(['.', '!', '?', '\n'], MaxSpokenChars);
            s = s[..(cut > 200 ? cut + 1 : MaxSpokenChars)] + "\n" + Loc.T("voice.restOnScreen");
        }
        return s;
    }

    public void Dispose()
    {
        Stop();
        _player?.Dispose();
    }
}
