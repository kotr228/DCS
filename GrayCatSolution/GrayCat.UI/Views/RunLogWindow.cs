namespace GrayCat.UI.Views;

using GrayCat.UI.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrayCat.UI.ViewModels;

/// <summary>Live output of npm and the project's dev server.</summary>
public class RunLogWindow : Controls.ThemedWindow
{
    private readonly TextBox _text;

    public RunLogWindow(MainViewModel viewModel)
    {
        Title = Loc.T("Run Log - GrayCat Studio");
        Width = 860;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _text = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.IsNullOrEmpty(viewModel.RunLog) ? Loc.T("Nothing has run yet. Use Project → Run App (F5).\n") : viewModel.RunLog
        };
        Content = _text;
        _text.ScrollToEnd();

        viewModel.RunOutput += OnOutput;
        Closed += (_, _) => viewModel.RunOutput -= OnOutput;
    }

    private void OnOutput(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _text.AppendText(line + "\n");
            _text.ScrollToEnd();
        });
    }
}
