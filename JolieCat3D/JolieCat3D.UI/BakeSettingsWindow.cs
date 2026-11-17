using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JolieCat3D.UI
{
    /// <summary>Settings for one bake, chosen in <see cref="BakeSettingsWindow"/>.</summary>
    public sealed record BakeSettings(int Resolution, int Samples, float Distance);

    /// <summary>Small modal dialog: texture size, ray count (AO only) and distance
    /// (AO reach, or the normal bake's cage distance).</summary>
    public sealed class BakeSettingsWindow : Window
    {
        private readonly ComboBox _resolution = new() { Width = 120 };
        private readonly ComboBox _samples = new() { Width = 120 };
        private readonly TextBox _distance = new() { Width = 120 };

        public BakeSettings? Result { get; private set; }

        public BakeSettingsWindow(string title, bool showSamples, string distanceLabel, float defaultDistance)
        {
            Title = title;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x22, 0x21));

            foreach (var r in new[] { 512, 1024, 2048, 4096 }) _resolution.Items.Add(r);
            _resolution.SelectedItem = 1024;
            foreach (var s in new[] { 16, 32, 64, 128, 256 }) _samples.Items.Add(s);
            _samples.SelectedItem = 64;
            _distance.Text = defaultDistance.ToString("0.###", CultureInfo.InvariantCulture);

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
                grid.Children.Add(text);
                grid.Children.Add(editor);
                row++;
            }

            AddRow("Texture size", _resolution);
            if (showSamples) AddRow("Rays per texel", _samples, "More rays = less noise, slower bake");
            AddRow(distanceLabel, _distance);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Bake", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
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
            var text = _distance.Text.Replace(',', '.');
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance) || distance <= 0)
            {
                MessageBox.Show(this, "Distance must be a positive number.", Title);
                return;
            }
            Result = new BakeSettings((int)_resolution.SelectedItem!, (int)_samples.SelectedItem!, distance);
            DialogResult = true;
        }
    }
}
