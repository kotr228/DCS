using AsmodayCat.UI.Localization;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;
using AsmodayCat.UI.Voice;

namespace AsmodayCat.UI.Views;

public partial class ApprovalWindow : Window
{
    private readonly DispatcherTimer   _countdown;
    private readonly VoiceConfirmation _voice = new();
    private bool _finished;

    public ApprovalRequestDto Request { get; }

    // (request id, approved)
    public event Action<Guid, bool>? Decided;

    public ApprovalWindow(ApprovalRequestDto request)
    {
        InitializeComponent();
        Request = request;

        TitleText.Text   = request.Title;
        SourceText.Text  = Loc.F("approval.source", request.Source);
        DetailsText.Text = request.Details;

        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) => UpdateCountdown();
        UpdateCountdown();
        _countdown.Start();

        // The interface language through Whisper when it's installed; otherwise the built-in English recognizer
        if (VoiceAssets.WhisperReady && SpeechInput.HasMicrophone)
        {
            Loaded += (_, _) => _ = ListenUkrainianAsync();
        }
        else if (_voice.TryStart())
        {
            VoiceText.Text = Loc.T("approval.listenEnglish");
            _voice.Decided += (approved, heard) => Dispatcher.InvokeAsync(() =>
            {
                VoiceText.Text = Loc.F("approval.heard", heard);
                Finish(approved);
            });
        }
        else
        {
            MicIcon.Kind   = MaterialDesignThemes.Wpf.PackIconKind.MicrophoneOff;
            VoiceText.Text = Loc.T("approval.noMic");
        }

        Loaded += (_, _) => SystemSounds.Exclamation.Play();
    }

    private readonly CancellationTokenSource _listen = new();

    // Listens phrase by phrase until a clear yes/no, a button press, or the request expires
    private async Task ListenUkrainianAsync()
    {
        var speech = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<SpeechInput>(App.Services);
        var prompt = Loc.T("approval.listen");
        VoiceText.Text = prompt;

        while (!_finished && !_listen.IsCancellationRequested)
        {
            string heard;
            try
            {
                heard = await speech.ListenAsync(null, _listen.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                VoiceText.Text = Loc.F("approval.voiceFailed", ex.Message);
                return;
            }
            if (_finished) return;

            switch (VoiceIntent.Parse(heard))
            {
                case bool approved:
                    VoiceText.Text = Loc.F("approval.heard", heard);
                    Finish(approved);
                    return;
                case null when heard.Length > 0:
                    VoiceText.Text = Loc.F("approval.notUnderstood", heard);
                    break;
                default:
                    VoiceText.Text = prompt;
                    break;
            }
        }
    }

    private void UpdateCountdown()
    {
        var left = (int)Math.Ceiling((Request.ExpiresAt - DateTime.Now).TotalSeconds);
        if (left <= 0)
        {
            CloseExpired();
            return;
        }
        CountdownText.Text = Loc.F("approval.countdown", left);
    }

    private void Approve_Click(object sender, RoutedEventArgs e) => Finish(true);
    private void Deny_Click(object sender, RoutedEventArgs e)    => Finish(false);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Finish(false);
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Finish(bool approved)
    {
        if (_finished) return;
        _finished = true;
        Decided?.Invoke(Request.Id, approved);
        Close();
    }

    // The service no longer waits for this request
    public void CloseExpired()
    {
        if (_finished) return;
        _finished = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _countdown.Stop();
        _listen.Cancel();
        _voice.Dispose();
        base.OnClosed(e);
    }
}
