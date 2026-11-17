using AsmodayCat.UI.Localization;
using System.IO;
using System.IO.Compression;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.Voice;

// Offline speech models, downloaded once on first use:
//   C:\ProgramData\CatSuite\AsmodayCat\voice\
// Not under the user profile on purpose: Piper crashes on non-ASCII paths (e.g. C:\Users\коте).
//     whisper\ggml-small.bin                    speech → text (Whisper small, ~490 MB)
//     piper\piper.exe (+ runtime files)         text → speech engine (~22 MB)
//     voices\<voice>.onnx                       voice of the interface language (60–80 MB):
//                                               uk_UA-ukrainian_tts, en_US-lessac, ru_RU-irina
public static class VoiceAssets
{
    private const string WhisperUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin";
    private const string PiperUrl   = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";
    private const string VoicesUrl  = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/";

    // Piper voice for each interface language: (folder in the voices repo, file name)
    private static (string Folder, string Name) Voice => Loc.Instance.Lang switch
    {
        "en" => ("en/en_US/lessac/medium/", "en_US-lessac-medium.onnx"),
        "ru" => ("ru/ru_RU/irina/medium/", "ru_RU-irina-medium.onnx"),
        _    => ("uk/uk_UA/ukrainian_tts/medium/", "uk_UA-ukrainian_tts-medium.onnx")
    };

    // The Ukrainian voice has several speakers; the others have one
    public static bool VoiceHasSpeakers => Loc.Instance.Lang is not ("en" or "ru");

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string Root { get; } = InitRoot();

    // Temp audio for the voice engine, also on an ASCII path
    public static string TempDir => Path.Combine(Root, "tmp");

    private static string InitRoot()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CatSuite", "AsmodayCat", "voice");

        // Earlier builds kept the models in %LOCALAPPDATA%: move them instead of downloading again
        var old = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatSuite", "AsmodayCat", "voice");
        try
        {
            if (Directory.Exists(old) && !Directory.Exists(root))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(root)!);
                Directory.Move(old, root);
            }
        }
        catch (IOException) { /* different drive or in use: files will be downloaded again */ }
        catch (UnauthorizedAccessException) { }

        return root;
    }

    public static string WhisperModel => Path.Combine(Root, "whisper", "ggml-small.bin");
    public static string PiperExe     => Path.Combine(Root, "piper", "piper.exe");
    public static string VoiceModel   => Path.Combine(Root, "voices", Voice.Name);

    public static bool WhisperReady => File.Exists(WhisperModel);
    public static bool PiperReady   => File.Exists(PiperExe) && File.Exists(VoiceModel) && File.Exists(VoiceModel + ".json");

    // progress: (what is downloading, 0..100)
    public static async Task EnsureWhisperAsync(IProgress<(string, double)>? progress, CancellationToken ct)
    {
        if (WhisperReady) return;
        await Gate.WaitAsync(ct);
        try
        {
            if (!WhisperReady)
                await Downloader.DownloadAsync(WhisperUrl, WhisperModel, Loc.T("voice.what.whisper"), progress, ct);
        }
        finally { Gate.Release(); }
    }

    public static async Task EnsurePiperAsync(IProgress<(string, double)>? progress, CancellationToken ct)
    {
        if (PiperReady) return;
        await Gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(PiperExe))
            {
                var zip = Path.Combine(Root, "piper.zip");
                await Downloader.DownloadAsync(PiperUrl, zip, Loc.T("voice.what.engine"), progress, ct);
                // The archive contains a top-level "piper" folder
                ZipFile.ExtractToDirectory(zip, Root, overwriteFiles: true);
                File.Delete(zip);
            }
            var (folder, name) = Voice;
            var model = Path.Combine(Root, "voices", name);
            if (!File.Exists(model))
                await Downloader.DownloadAsync(VoicesUrl + folder + name, model, Loc.T("voice.what.voice"), progress, ct);
            if (!File.Exists(model + ".json"))
                await Downloader.DownloadAsync(VoicesUrl + folder + name + ".json", model + ".json", Loc.T("voice.what.voice"), progress, ct);
        }
        finally { Gate.Release(); }
    }
}
