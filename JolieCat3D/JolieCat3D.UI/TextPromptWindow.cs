using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JolieCat3D.UI
{
    /// <summary>Tiny modal "enter a name" dialog (rename action, etc.).</summary>
    public sealed class TextPromptWindow : Window
    {
        private readonly TextBox _text = new() { MinWidth = 220 };

        public string? Result { get; private set; }

        public TextPromptWindow(string title, string label, string initial)
        {
            Title = title;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x22, 0x21));

            _text.Text = initial;
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 0, 0, 6) });
            panel.Children.Add(_text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(10, 3, 10, 3) };
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_text.Text)) return;
                Result = _text.Text.Trim();
                DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
            Loaded += (_, _) => { _text.Focus(); _text.SelectAll(); };
        }
    }
}
