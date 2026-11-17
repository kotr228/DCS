using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JolieCat3D.UI
{
    /// <summary>Options for Add > Text.</summary>
    public sealed record TextOptions(string Text, string Font, float Height, float Depth, bool Bold, bool Italic);

    /// <summary>Modal dialog for Add > Text (3D text from an installed font).</summary>
    public sealed class AddTextWindow : Window
    {
        private readonly TextBox _text = new() { Width = 220, Text = "Text" };
        private readonly ComboBox _font = new() { Width = 220 };
        private readonly TextBox _height = new() { Width = 90, Text = "1" };
        private readonly TextBox _depth = new() { Width = 90, Text = "0.2" };
        private readonly CheckBox _bold = new() { Content = "Bold", Foreground = Brushes.Gainsboro };
        private readonly CheckBox _italic = new() { Content = "Italic", Foreground = Brushes.Gainsboro, Margin = new Thickness(10, 0, 0, 0) };

        public TextOptions? Result { get; private set; }

        public AddTextWindow()
        {
            Title = "Add Text";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x22, 0x21));

            foreach (var family in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n)) _font.Items.Add(family);
            var defaultFont = _font.Items.Cast<string>().FirstOrDefault(n => n == "Segoe UI") ?? _font.Items.Cast<string>().FirstOrDefault() ?? "Arial";

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var row = 0;
            void AddRow(string label, FrameworkElement editor)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var text = new TextBlock { Text = label, Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(text, row);
                Grid.SetRow(editor, row);
                Grid.SetColumn(editor, 1);
                editor.Margin = new Thickness(0, 4, 0, 4);
                editor.HorizontalAlignment = HorizontalAlignment.Left;
                grid.Children.Add(text);
                grid.Children.Add(editor);
                row++;
            }
            var style = new StackPanel { Orientation = Orientation.Horizontal };
            style.Children.Add(_bold);
            style.Children.Add(_italic);
            AddRow("Text", _text);
            AddRow("Font", _font);
            AddRow("Height", _height);
            AddRow("Depth", _depth);
            AddRow("Style", style);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Add", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(10, 3, 10, 3) };
            ok.Click += (_, _) => Accept();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(buttons, row);
            Grid.SetColumnSpan(buttons, 2);
            grid.Children.Add(buttons);
            Content = grid;
            Loaded += (_, _) =>
            {
                _font.SelectedItem = defaultFont;
                _text.Focus();
                _text.SelectAll();
            };
        }

        private void Accept()
        {
            static bool Float(TextBox box, float min, out float value) =>
                float.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min;
            if (string.IsNullOrWhiteSpace(_text.Text) || !Float(_height, 0.001f, out var height) || !Float(_depth, 0f, out var depth))
            {
                MessageBox.Show(this, "Enter some text, a positive height and a depth of 0 or more.", Title);
                return;
            }
            Result = new TextOptions(_text.Text, _font.SelectedItem as string ?? "Segoe UI", height, depth, _bold.IsChecked == true, _italic.IsChecked == true);
            DialogResult = true;
        }
    }
}
