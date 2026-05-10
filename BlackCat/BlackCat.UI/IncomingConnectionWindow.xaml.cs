using System.Windows;
using System.Windows.Threading;
using BlackCat.NetworkCore;

namespace BlackCat.UI;

/// <summary>
/// Toast-повідомлення у правому нижньому куті екрана з кнопками Прийняти / Відхилити.
/// </summary>
public partial class IncomingConnectionWindow : Window
{
    private readonly IncomingConnectionRequestEventArgs _request;
    private readonly DispatcherTimer _countdownTimer;
    private int _secondsLeft = 60;
    private bool _answered;

    public IncomingConnectionWindow(IncomingConnectionRequestEventArgs request)
    {
        _request = request;
        InitializeComponent();

        PeerBlackIDText.Text = request.PeerBlackID;
        PeerIPText.Text = request.PeerIP;

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += CountdownTick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Позиціонувати у правий нижній кут
        var screen = SystemParameters.WorkArea;
        Left = screen.Right - Width - 16;
        Top = screen.Bottom - ActualHeight - 16;

        _countdownTimer.Start();

        // Анімація появи
        Opacity = 0;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
        BeginAnimation(OpacityProperty, fade);
    }

    private void CountdownTick(object? sender, EventArgs e)
    {
        _secondsLeft--;
        TimerText.Text = $"Автовідхилення через {_secondsLeft} сек...";

        if (_secondsLeft <= 0)
            Reject();
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e) => Accept();
    private void RejectButton_Click(object sender, RoutedEventArgs e) => Reject();

    private void Accept()
    {
        if (_answered) return;
        _answered = true;
        _countdownTimer.Stop();
        _request.ApprovalSource.TrySetResult(true);
        CloseAnimated();
    }

    private void Reject()
    {
        if (_answered) return;
        _answered = true;
        _countdownTimer.Stop();
        _request.ApprovalSource.TrySetResult(false);
        CloseAnimated();
    }

    private void CloseAnimated()
    {
        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        _countdownTimer.Stop();
        // Якщо вікно закрили без відповіді — відхилити
        _request.ApprovalSource.TrySetResult(false);
        base.OnClosed(e);
    }
}
