using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JolieCat3D.UI
{
    /// <summary>Options for Scatter.</summary>
    public sealed record ScatterOptions(int Count, int Seed, float MinDistance, float ScaleMin, float ScaleMax, bool AlignToNormal, bool RandomRotation);

    /// <summary>Modal dialog for Object > Scatter Selected on Active.</summary>
    public sealed class ScatterWindow : Window
    {
        private readonly TextBox _count = new() { Width = 90, Text = "50" };
        private readonly TextBox _seed = new() { Width = 90, Text = "1" };
        private readonly TextBox _distance = new() { Width = 90, Text = "0" };
        private readonly TextBox _scaleMin = new() { Width = 90, Text = "0.8" };
        private readonly TextBox _scaleMax = new() { Width = 90, Text = "1.2" };
        private readonly CheckBox _align = new() { Content = "Align to surface normal", IsChecked = true, Foreground = Brushes.Gainsboro };
        private readonly CheckBox _rotate = new() { Content = "Random rotation", IsChecked = true, Foreground = Brushes.Gainsboro };

        public ScatterOptions? Result { get; private set; }

        public ScatterWindow()
        {
            Title = "Scatter";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x22, 0x21));

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var row = 0;
            void AddRow(string label, FrameworkElement editor, string? tip = null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var text = new TextBlock { Text = label, Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
                Grid.SetRow(text, row);
                Grid.SetRow(editor, row);
                Grid.SetColumn(editor, 1);
                editor.Margin = new Thickness(0, 4, 0, 4);
                editor.HorizontalAlignment = HorizontalAlignment.Left;
                grid.Children.Add(text);
                grid.Children.Add(editor);
                row++;
            }
            AddRow("Count", _count, "How many instances");
            AddRow("Seed", _seed, "Change for a different random layout");
            AddRow("Min distance", _distance, "Keep instances at least this far apart (0 = off)");
            AddRow("Scale min", _scaleMin);
            AddRow("Scale max", _scaleMax);
            AddRow("", _align);
            AddRow("", _rotate);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Scatter", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(10, 3, 10, 3) };
            ok.Click += (_, _) => Accept();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(buttons, row);
            Grid.SetColumnSpan(buttons, 2);
            grid.Children.Add(buttons);
            Content = grid;
        }

        private void Accept()
        {
            static bool F(TextBox b, out float v) => float.TryParse(b.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            if (!int.TryParse(_count.Text, out var count) || count < 1 || count > 100000 || !int.TryParse(_seed.Text, out var seed)
                || !F(_distance, out var distance) || distance < 0 || !F(_scaleMin, out var smin) || !F(_scaleMax, out var smax) || smin <= 0 || smax < smin)
            {
                MessageBox.Show(this, "Count 1-100000, distance >= 0, 0 < scale min <= scale max.", Title);
                return;
            }
            Result = new ScatterOptions(count, seed, distance, smin, smax, _align.IsChecked == true, _rotate.IsChecked == true);
            DialogResult = true;
        }
    }
}
