using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JolieCat3D.Engine.Rendering;
using Microsoft.Win32;

namespace JolieCat3D.UI
{
    /// <summary>
    /// Render Result window (F12): runs the <see cref="PathTracer"/> progressively on a
    /// background thread, showing the image as samples accumulate, until the sample
    /// target is reached or Stop is pressed. Save writes a PNG.
    /// </summary>
    public sealed class RenderWindow : Window
    {
        private readonly PathTracer _tracer;
        private readonly int _targetSamples;
        private readonly Image _image = new() { Stretch = Stretch.Uniform };
        private readonly TextBlock _status = new() { Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        private readonly Button _stop = new() { Content = "Stop", Padding = new Thickness(12, 3, 12, 3) };
        private readonly CheckBox _denoise = new() { Content = "Denoise", IsChecked = true, Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        private volatile bool _denoiseEnabled = true;
        private readonly CancellationTokenSource _cancel = new();
        private readonly Stopwatch _clock = new();
        private Task? _worker;

        public RenderWindow(PathTracer tracer, int targetSamples, string title)
        {
            _tracer = tracer;
            _targetSamples = Math.Max(1, targetSamples);
            Title = title;
            Width = Math.Min(1400, tracer.Width + 40);
            Height = Math.Min(900, tracer.Height + 110);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1B, 0x1A));
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

            var save = new Button { Content = "Save PNG...", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(6, 0, 0, 0) };
            _stop.Click += (_, _) => _cancel.Cancel();
            save.Click += (_, _) => Save();
            _denoise.Click += (_, _) =>
            {
                _denoiseEnabled = _denoise.IsChecked == true;
                if (_stop.IsEnabled == false) _image.Source = _tracer.ToBitmap(_denoiseEnabled);
            };

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            bar.Children.Add(_stop);
            bar.Children.Add(save);
            bar.Children.Add(_denoise);
            bar.Children.Add(_status);

            var root = new DockPanel();
            DockPanel.SetDock(bar, Dock.Bottom);
            root.Children.Add(bar);
            root.Children.Add(new Border { Background = Brushes.Black, Margin = new Thickness(8, 8, 8, 0), Child = _image });
            Content = root;

            Loaded += (_, _) => Start();
            Closed += (_, _) => _cancel.Cancel();
        }

        private void Start()
        {
            _clock.Start();
            UpdateStatus(rendering: true);
            var token = _cancel.Token;
            _worker = Task.Run(() =>
            {
                var lastShown = Stopwatch.StartNew();
                try
                {
                    while (!token.IsCancellationRequested && _tracer.SampleCount < _targetSamples)
                    {
                        _tracer.RenderPass(token);
                        // Refresh at most ~4 times a second (and on the first few passes).
                        if (_tracer.SampleCount <= 4 || lastShown.ElapsedMilliseconds > 250 || _tracer.SampleCount >= _targetSamples)
                        {
                            var bitmap = _tracer.ToBitmap(_denoiseEnabled);
                            Dispatcher.BeginInvoke(() => { _image.Source = bitmap; UpdateStatus(rendering: true); });
                            lastShown.Restart();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                Dispatcher.BeginInvoke(() =>
                {
                    _clock.Stop();
                    _image.Source = _tracer.ToBitmap(_denoiseEnabled);
                    UpdateStatus(rendering: false);
                    _stop.IsEnabled = false;
                });
            });
        }

        private void UpdateStatus(bool rendering)
        {
            var state = rendering ? "Rendering" : "Done";
            _status.Text = $"{state}: {_tracer.SampleCount}/{_targetSamples} samples   {_tracer.Width}x{_tracer.Height}   {_tracer.TriangleCount:N0} triangles   {_clock.Elapsed:mm\\:ss}";
        }

        private void Save()
        {
            var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = "render.png", Title = "Save Render" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(_tracer.ToBitmap(_denoiseEnabled)));
                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);
                _status.Text = "Saved " + Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Save Render");
            }
        }
    }
}
