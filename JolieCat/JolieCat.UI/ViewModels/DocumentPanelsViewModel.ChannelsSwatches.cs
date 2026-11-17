using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>One color in the Swatches panel.</summary>
    public sealed class SwatchItem
    {
        public SwatchItem(Color color)
        {
            Color = color;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Brush = brush;
        }

        public Color Color { get; }
        public Brush Brush { get; }
        public string Hex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
    }

    /// <summary>One row of the Channels panel: a color channel (or the RGB
    /// composite), or a saved selection (an alpha channel).</summary>
    public sealed partial class ChannelItem : ObservableObject
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>0 = RGB, 1..3 = Red, Green, Blue; -1 for a saved selection.</summary>
        public int Channel { get; init; } = -1;

        public string? SelectionName { get; init; }

        public string Shortcut { get; init; } = string.Empty;

        [ObservableProperty] private BitmapSource? thumbnail;
        [ObservableProperty] private bool isViewed;
    }

    public partial class DocumentPanelsViewModel
    {
        [ObservableProperty] private bool showSwatches;
        [ObservableProperty] private bool showChannels;

        partial void OnShowChannelsChanged(bool value) { if (value) { _channelsOf = null; Refresh(); } }

        // ----- Swatches -----

        /// <summary>Where the swatches are kept (shared by every document, kept between runs).</summary>
        public static string SwatchesFile { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat", "swatches.json");

        private ObservableCollection<SwatchItem>? _swatches;

        public ObservableCollection<SwatchItem> Swatches => _swatches ??= LoadSwatches();

        /// <summary>A small starter palette: grays, then hues in rows of light, full, dark.</summary>
        internal static IEnumerable<Color> DefaultSwatches()
        {
            foreach (var v in new byte[] { 0, 64, 128, 192, 255 }) yield return Color.FromRgb(v, v, v);
            var hues = new[] { 0, 30, 55, 120, 180, 210, 265, 300 };
            foreach (var (s, l) in new[] { (0.55, 0.8), (0.9, 0.5), (0.8, 0.3) })
                foreach (var h in hues) yield return FromHsl(h, s, l);
        }

        private static Color FromHsl(double h, double s, double l)
        {
            var c = (1 - Math.Abs(2 * l - 1)) * s;
            var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            var m = l - c / 2;
            var (r, g, b) = h switch
            {
                < 60 => (c, x, 0.0),
                < 120 => (x, c, 0.0),
                < 180 => (0.0, c, x),
                < 240 => (0.0, x, c),
                < 300 => (x, 0.0, c),
                _ => (c, 0.0, x),
            };
            static byte B(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
            return Color.FromRgb(B(r + m), B(g + m), B(b + m));
        }

        private static ObservableCollection<SwatchItem> LoadSwatches()
        {
            try
            {
                if (File.Exists(SwatchesFile) && JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SwatchesFile)) is { } saved)
                    return new ObservableCollection<SwatchItem>(saved.Select(Parse).OfType<Color>().Select(c => new SwatchItem(c)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
            return new ObservableCollection<SwatchItem>(DefaultSwatches().Select(c => new SwatchItem(c)));
        }

        private static Color? Parse(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch (FormatException) { return null; }
        }

        private void SaveSwatches()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SwatchesFile)!);
                File.WriteAllText(SwatchesFile, JsonSerializer.Serialize(Swatches.Select(s => s.Hex).ToList()));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        /// <summary>Click: the swatch becomes the foreground color.</summary>
        [RelayCommand]
        private void PickSwatch(SwatchItem? swatch)
        {
            if (swatch is not null && _document()?.Canvas is { } canvas) canvas.PrimaryColor = swatch.Color;
        }

        /// <summary>Ctrl+click: the swatch becomes the background color.</summary>
        [RelayCommand]
        private void PickBackgroundSwatch(SwatchItem? swatch)
        {
            if (swatch is not null && _document()?.Canvas is { } canvas) canvas.SecondaryColor = swatch.Color;
        }

        /// <summary>+: the foreground color as a new swatch (once).</summary>
        [RelayCommand]
        private void AddSwatch()
        {
            if (_document()?.Canvas is not { } canvas) return;
            var color = canvas.PrimaryColor;
            color.A = 255;
            if (Swatches.Any(s => s.Color == color)) return;
            Swatches.Add(new SwatchItem(color));
            SaveSwatches();
        }

        /// <summary>Right-click: removes the swatch.</summary>
        [RelayCommand]
        private void RemoveSwatch(SwatchItem? swatch)
        {
            if (swatch is null || !Swatches.Remove(swatch)) return;
            SaveSwatches();
        }

        [RelayCommand]
        private void ResetSwatches()
        {
            Swatches.Clear();
            foreach (var c in DefaultSwatches()) Swatches.Add(new SwatchItem(c));
            SaveSwatches();
        }

        // ----- Channels -----

        public ObservableCollection<ChannelItem> Channels { get; } = new();

        private object? _channelsOf;
        private long _channelsVersion = -1;
        private string _channelsSelections = string.Empty;

        /// <summary>Rebuilds the channel thumbnails when the image or the saved selections changed.</summary>
        private void UpdateChannels(DocumentViewModel doc)
        {
            if (!ShowChannels || _small is null) return;
            var scene = doc.Layers.Scene;
            var names = string.Join("\n", scene.SavedSelections.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
            if (ReferenceEquals(_channelsOf, doc) && _channelsVersion == doc.Layers.ContentVersion && names == _channelsSelections)
            {
                MarkViewedChannel(doc.Canvas.ViewChannel);
                return;
            }
            _channelsOf = doc;
            _channelsVersion = doc.Layers.ContentVersion;
            _channelsSelections = names;

            Channels.Clear();
            Channels.Add(new ChannelItem { Name = "RGB", Channel = 0, Shortcut = "Ctrl+2", Thumbnail = SkiaImageThumb(_small, -1) });
            Channels.Add(new ChannelItem { Name = "Red", Channel = 1, Shortcut = "Ctrl+3", Thumbnail = SkiaImageThumb(_small, 0) });
            Channels.Add(new ChannelItem { Name = "Green", Channel = 2, Shortcut = "Ctrl+4", Thumbnail = SkiaImageThumb(_small, 1) });
            Channels.Add(new ChannelItem { Name = "Blue", Channel = 3, Shortcut = "Ctrl+5", Thumbnail = SkiaImageThumb(_small, 2) });
            foreach (var (name, svg) in scene.SavedSelections.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                Channels.Add(new ChannelItem { Name = name, SelectionName = name, Thumbnail = SelectionThumb(svg, doc.Layers.DocumentWidth, doc.Layers.DocumentHeight) });
            MarkViewedChannel(doc.Canvas.ViewChannel);
        }

        private void MarkViewedChannel(int viewed)
        {
            foreach (var c in Channels) c.IsViewed = c.SelectionName is null && c.Channel == viewed;
        }

        /// <summary>The thumbnail as color (<paramref name="channel"/> -1) or one channel as gray.</summary>
        private static BitmapSource SkiaImageThumb(SKBitmap small, int channel)
        {
            if (channel < 0) return Timeline.TimelineViewModel.FastThumbnail(small, 40);
            using var gray = new SKBitmap(small.Width, small.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(gray))
            using (var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(ChannelMatrix(channel + 1)) })
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(small, 0, 0, paint);
            }
            return Timeline.TimelineViewModel.FastThumbnail(gray, 40);
        }

        private static BitmapSource SelectionThumb(string svg, int width, int height)
        {
            var scale = Math.Min(40f / Math.Max(1, width), 40f / Math.Max(1, height));
            using var picture = new SKBitmap(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(picture))
            using (var path = SKPath.ParseSvgPathData(svg) ?? new SKPath())
            using (var white = new SKPaint { Color = SKColors.White, IsAntialias = true })
            {
                canvas.Clear(SKColors.Black);
                canvas.Scale(scale);
                canvas.DrawPath(path, white);
            }
            return Timeline.TimelineViewModel.FastThumbnail(picture, 40);
        }

        /// <summary>A color matrix that shows channel 1..3 (red, green, blue) as gray, keeping alpha.</summary>
        public static float[] ChannelMatrix(int channel)
        {
            float r = channel == 1 ? 1 : 0, g = channel == 2 ? 1 : 0, b = channel == 3 ? 1 : 0;
            return new[]
            {
                r, g, b, 0, 0,
                r, g, b, 0, 0,
                r, g, b, 0, 0,
                0, 0, 0, 1, 0,
            };
        }

        /// <summary>Click on a row: a color channel is shown alone on the canvas (RGB: all
        /// of them again); a saved selection is loaded as the selection.</summary>
        [RelayCommand]
        private void ChooseChannel(ChannelItem? item)
        {
            if (item is null || _document() is not { } doc) return;
            if (item.SelectionName is { } name)
            {
                doc.Canvas.LoadSelection(name);
                return;
            }
            doc.Canvas.ViewChannel = item.Channel;
            MarkViewedChannel(item.Channel);
        }

        /// <summary>Deletes a saved selection (its row's ✕).</summary>
        [RelayCommand]
        private void DeleteSavedSelection(ChannelItem? item)
        {
            if (item?.SelectionName is not { } name || _document() is not { } doc) return;
            if (doc.Layers.Scene.SavedSelections.Remove(name))
            {
                doc.Layers.History.MarkModified();
                Refresh();
            }
        }
    }
}
