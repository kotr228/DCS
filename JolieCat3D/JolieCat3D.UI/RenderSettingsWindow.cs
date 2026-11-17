using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JolieCat3D.UI
{
    /// <summary>Final render options chosen before F12 / Render Image.</summary>
    public sealed record FinalRenderOptions(int Width, int Samples, int Bounces, float SkyStrength, float Exposure);

    /// <summary>Small modal dialog for <see cref="FinalRenderOptions"/>.</summary>
    public sealed class RenderSettingsWindow : Window
    {
        private readonly TextBox _width = new() { Width = 90 };
        private readonly TextBox _samples = new() { Width = 90 };
        private readonly TextBox _bounces = new() { Width = 90 };
        private readonly TextBox _sky = new() { Width = 90 };
        private readonly TextBox _exposure = new() { Width = 90 };

        public FinalRenderOptions? Result { get; private set; }

        public RenderSettingsWindow(FinalRenderOptions current)
        {
            Title = "Render Settings";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x22, 0x21));

            _width.Text = current.Width.ToString(CultureInfo.InvariantCulture);
            _samples.Text = current.Samples.ToString(CultureInfo.InvariantCulture);
            _bounces.Text = current.Bounces.ToString(CultureInfo.InvariantCulture);
            _sky.Text = current.SkyStrength.ToString("0.##", CultureInfo.InvariantCulture);
            _exposure.Text = current.Exposure.ToString("0.##", CultureInfo.InvariantCulture);

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var row = 0;
            void AddRow(string label, FrameworkElement editor, string tip)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var text = new TextBlock { Text = label, Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
                Grid.SetRow(text, row);
                Grid.SetRow(editor, row);
                Grid.SetColumn(editor, 1);
                editor.Margin = new Thickness(0, 4, 0, 4);
                editor.ToolTip = tip;
                grid.Children.Add(text);
                grid.Children.Add(editor);
                row++;
            }
            AddRow("Width (px)", _width, "Image width; height follows the viewport aspect ratio");
            AddRow("Samples", _samples, "Samples per pixel - more = less noise, slower");
            AddRow("Light bounces", _bounces, "Indirect light bounces (global illumination)");
            AddRow("Sky strength", _sky, "Brightness of the sky light (0 = black world)");
            AddRow("Exposure", _exposure, "Overall brightness multiplier");

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Render", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
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
            static bool Int(TextBox box, int min, int max, out int value) =>
                int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
            static bool Float(TextBox box, float min, float max, out float value) =>
                float.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

            if (!Int(_width, 16, 7680, out var width) || !Int(_samples, 1, 100000, out var samples) || !Int(_bounces, 0, 64, out var bounces)
                || !Float(_sky, 0, 100, out var sky) || !Float(_exposure, 0.01f, 100, out var exposure))
            {
                MessageBox.Show(this, "Width 16-7680, samples 1-100000, bounces 0-64, sky 0-100, exposure 0.01-100.", Title);
                return;
            }
            Result = new FinalRenderOptions(width, samples, bounces, sky, exposure);
            DialogResult = true;
        }
    }
}
