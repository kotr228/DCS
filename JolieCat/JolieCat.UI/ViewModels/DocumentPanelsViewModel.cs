using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Rendering;
using JolieCat.UI.Media;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// The Navigator, Info and Histogram panels (tabs next to Properties). They read
    /// the active document a few times a second, but only while one of them is shown,
    /// and only redo the (costly) composite after the image has actually changed.
    /// </summary>
    /// <summary>One Color Sampler's line in the Info panel.</summary>
    public sealed partial class SamplerReadout : ObservableObject
    {
        [ObservableProperty] private string label = string.Empty;
        [ObservableProperty] private string text = string.Empty;
        [ObservableProperty] private System.Windows.Media.Brush swatch = System.Windows.Media.Brushes.Transparent;

        private SKColor _color;
        private bool _hasColor;

        internal void Update(int number, int x, int y, SKColor c)
        {
            Label = $"#{number}";
            Text = $"R {c.Red,3}  G {c.Green,3}  B {c.Blue,3}   ({x}, {y})";
            if (_hasColor && c == _color) return;
            _color = c;
            _hasColor = true;
            var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
            brush.Freeze();
            Swatch = brush;
        }
    }

    public partial class DocumentPanelsViewModel : ObservableObject
    {
        public const int ThumbnailSize = 240;

        private readonly Func<DocumentViewModel?> _document;
        private readonly DispatcherTimer? _timer;
        private object? _compositeOf;
        private long _compositeVersion = -1;
        private SKBitmap? _composite;      // full size, for Info's color readout
        private SKBitmap? _small;          // thumbnail, for the Navigator and Histogram
        private float _thumbScale = 1;

        public DocumentPanelsViewModel(Func<DocumentViewModel?> document, bool startTimer = true)
        {
            _document = document;
            if (!startTimer) return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, _) => { if (!ShowProperties) Refresh(); };
            _timer.Start();
        }

        // ----- Which tab is shown -----

        [ObservableProperty] private bool showProperties = true;
        [ObservableProperty] private bool showNavigator;
        [ObservableProperty] private bool showInfo;
        [ObservableProperty] private bool showHistogram;

        partial void OnShowNavigatorChanged(bool value) { if (value) Refresh(); }
        partial void OnShowInfoChanged(bool value) { if (value) Refresh(); }
        partial void OnShowHistogramChanged(bool value) { if (value) Refresh(); }

        // ----- Navigator -----

        [ObservableProperty] private BitmapSource? thumbnail;
        [ObservableProperty] private double thumbnailWidth;
        [ObservableProperty] private double thumbnailHeight;
        [ObservableProperty] private Rect viewBox;
        [ObservableProperty] private double zoomPercent = 100;

        private bool _settingZoom;

        partial void OnZoomPercentChanged(double value)
        {
            if (_settingZoom || _document()?.Canvas is not { } canvas) return;
            canvas.ZoomFromNavigator(value / 100);
        }

        /// <summary>Navigator click/drag at a point of the thumbnail.</summary>
        public void NavigateTo(double x, double y)
        {
            if (_document()?.Canvas is not { } canvas || _thumbScale <= 0) return;
            canvas.CenterViewOn(new SKPoint((float)(x / _thumbScale), (float)(y / _thumbScale)));
            UpdateViewBox(canvas);
        }

        // ----- Info -----

        [ObservableProperty] private string colorText = "-";
        [ObservableProperty] private string hexText = "-";
        [ObservableProperty] private string positionText = "-";
        [ObservableProperty] private string documentText = "-";
        [ObservableProperty] private string selectionText = "none";
        [ObservableProperty] private System.Windows.Media.Brush colorSwatch = System.Windows.Media.Brushes.Transparent;

        /// <summary>One line per Color Sampler point.</summary>
        public System.Collections.ObjectModel.ObservableCollection<SamplerReadout> Samplers { get; } = new();

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void ClearSamplers()
        {
            _document()?.Canvas.ClearColorSamplers();
            Samplers.Clear();
        }

        // ----- Histogram -----

        [ObservableProperty] private string lumaPoints = string.Empty;
        [ObservableProperty] private string redPoints = string.Empty;
        [ObservableProperty] private string greenPoints = string.Empty;
        [ObservableProperty] private string bluePoints = string.Empty;
        [ObservableProperty] private string statsText = string.Empty;

        /// <summary>Re-reads the active document (called by the timer; tests call it directly).</summary>
        public void Refresh()
        {
            if (_document() is not { } doc) return;
            var layers = doc.Layers;
            int w = layers.DocumentWidth, h = layers.DocumentHeight;

            if (!ReferenceEquals(_compositeOf, doc) || _compositeVersion != layers.ContentVersion || _composite is null
                || _composite.Width != w || _composite.Height != h)
            {
                _composite?.Dispose();
                _small?.Dispose();
                _composite = SceneCompositor.Flatten(layers.Scene, w, h);
                _thumbScale = Math.Min(1f, (float)ThumbnailSize / Math.Max(w, h));
                _small = _composite.Resize(new SKImageInfo(Math.Max(1, (int)(w * _thumbScale)), Math.Max(1, (int)(h * _thumbScale))),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                _compositeOf = doc;
                _compositeVersion = layers.ContentVersion;

                Thumbnail = SkiaImageConverter.ToThumbnail(_small, ThumbnailSize);
                ThumbnailWidth = _small.Width;
                ThumbnailHeight = _small.Height;
                UpdateHistogram(_small);
            }

            UpdateViewBox(doc.Canvas);
            UpdateInfo(doc, w, h);
        }

        private void UpdateViewBox(CanvasViewModel canvas)
        {
            var area = canvas.VisibleDocumentArea;
            var s = _thumbScale;
            var box = new Rect(area.Left * s, area.Top * s, Math.Max(1, area.Width * s), Math.Max(1, area.Height * s));
            box.Intersect(new Rect(0, 0, ThumbnailWidth, ThumbnailHeight));
            ViewBox = box.IsEmpty ? new Rect(0, 0, 0, 0) : box;
            _settingZoom = true;
            ZoomPercent = Math.Round(canvas.Zoom * 100, 1);
            _settingZoom = false;
        }

        private void UpdateInfo(DocumentViewModel doc, int w, int h)
        {
            DocumentText = $"{w} × {h} px";
            var selection = doc.Layers.Scene.Selection;
            SelectionText = selection.HasSelection && selection.Region is { } region
                ? $"{region.Bounds.Width} × {region.Bounds.Height} px at {region.Bounds.Left}, {region.Bounds.Top}"
                : "none";

            UpdateSamplers(doc, w, h);

            if (doc.Canvas.HoverDocumentPoint is { } p && _composite is not null
                && p.X >= 0 && p.Y >= 0 && p.X < w && p.Y < h)
            {
                var c = _composite.GetPixel((int)p.X, (int)p.Y);
                PositionText = $"X {(int)p.X}   Y {(int)p.Y}";
                ColorText = $"R {c.Red}   G {c.Green}   B {c.Blue}   A {c.Alpha}";
                HexText = $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
                var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
                brush.Freeze();
                ColorSwatch = brush;
            }
            else
            {
                PositionText = "-";
                ColorText = "move the pointer over the image";
                HexText = "-";
                ColorSwatch = System.Windows.Media.Brushes.Transparent;
            }
        }

        private void UpdateSamplers(DocumentViewModel doc, int w, int h)
        {
            var points = doc.Canvas.ColorSamplers;
            while (Samplers.Count > points.Count) Samplers.RemoveAt(Samplers.Count - 1);
            while (Samplers.Count < points.Count) Samplers.Add(new SamplerReadout());
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var c = _composite is not null && p.X >= 0 && p.Y >= 0 && p.X < w && p.Y < h
                    ? _composite.GetPixel((int)p.X, (int)p.Y)
                    : SKColors.Transparent;
                Samplers[i].Update(i + 1, (int)p.X, (int)p.Y, c);
            }
        }

        private void UpdateHistogram(SkiaSharp.SKBitmap image)
        {
            var luma = new int[256];
            var red = new int[256];
            var green = new int[256];
            var blue = new int[256];
            long sum = 0;
            var count = 0;
            foreach (var c in image.Pixels)
            {
                if (c.Alpha == 0) continue;
                var l = (int)Math.Round(0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue);
                luma[l]++; red[c.Red]++; green[c.Green]++; blue[c.Blue]++;
                sum += l;
                count++;
            }

            var max = 1;
            for (var i = 0; i < 256; i++) max = Math.Max(max, Math.Max(luma[i], Math.Max(red[i], Math.Max(green[i], blue[i]))));
            LumaPoints = Points(luma, max);
            RedPoints = Points(red, max);
            GreenPoints = Points(green, max);
            BluePoints = Points(blue, max);

            if (count == 0)
            {
                StatsText = "No visible pixels";
                return;
            }
            var mean = (double)sum / count;
            double variance = 0;
            for (var i = 0; i < 256; i++) variance += luma[i] * (i - mean) * (i - mean);
            var median = 0;
            for (int acc = 0; median < 255; median++) { acc += luma[median]; if (acc >= count / 2) break; }
            StatsText = string.Format(CultureInfo.InvariantCulture, "Mean {0:0.0}   Std dev {1:0.0}   Median {2}   Pixels {3:N0}",
                mean, Math.Sqrt(variance / count), median, count);
        }

        /// <summary>A polygon outline over a 256 x 100 box (square-root scaled so small
        /// counts still show).</summary>
        private static string Points(int[] counts, int max)
        {
            var sb = new StringBuilder("0,100 ");
            var top = Math.Sqrt(max);
            for (var i = 0; i < 256; i++)
                sb.Append(CultureInfo.InvariantCulture, $"{i},{100 - Math.Sqrt(counts[i]) / top * 100:0.#} ");
            sb.Append("255,100");
            return sb.ToString();
        }
    }
}
