using System.Globalization;
using System.Speech.Recognition;

namespace AsmodayCat.UI.Services;

// Listens for a spoken yes/no while an approval window is open.
// Uses the offline Windows recognizer (English), so it works without internet.
public sealed class VoiceConfirmation : IDisposable
{
    // Approve needs a deliberate word and higher confidence than deny,
    // so background talk is less likely to approve something by accident.
    private static readonly string[] ApproveWords = ["approve", "confirm", "yes approve", "yes confirm"];
    private static readonly string[] DenyWords    = ["no", "deny", "cancel", "stop", "reject"];

    private const float ApproveConfidence = 0.80f;
    private const float DenyConfidence    = 0.60f;

    private SpeechRecognitionEngine? _engine;

    // true = approve, false = deny; raised on a background thread
    public event Action<bool, string>? Decided;

    public bool TryStart()
    {
        try
        {
            var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            var info = recognizers.FirstOrDefault(r => r.Culture.Name == "en-US")
                       ?? recognizers.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en");
            if (info is null) return false;

            _engine = new SpeechRecognitionEngine(info);

            var choices = new Choices();
            choices.Add(ApproveWords);
            choices.Add(DenyWords);
            _engine.LoadGrammar(new Grammar(new GrammarBuilder(choices) { Culture = info.Culture }));

            _engine.SetInputToDefaultAudioDevice();   // throws when there is no microphone
            _engine.SpeechRecognized += OnRecognized;
            _engine.RecognizeAsync(RecognizeMode.Multiple);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException
                                       or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            Dispose();
            return false;
        }
    }

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        var text = e.Result.Text.ToLower(CultureInfo.InvariantCulture);

        if (ApproveWords.Contains(text) && e.Result.Confidence >= ApproveConfidence)
            Decided?.Invoke(true, text);
        else if (DenyWords.Contains(text) && e.Result.Confidence >= DenyConfidence)
            Decided?.Invoke(false, text);
    }

    public void Dispose()
    {
        if (_engine is null) return;
        try
        {
            _engine.SpeechRecognized -= OnRecognized;
            _engine.RecognizeAsyncCancel();
        }
        catch (InvalidOperationException) { }
        _engine.Dispose();
        _engine = null;
    }
}
