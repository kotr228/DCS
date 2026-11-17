using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Backs the Vanishing Point dialog: the surface's four corners (dragged on a preview
    /// of the image), the picture to lay on it and where; the preview is redrawn on every change.
    /// </summary>
    public sealed partial class VanishingPointViewModel : ObservableObject, IDisposable
    {
        public const int PreviewMax = 760;

        private readonly SKBitmap _document;     // the image, small, for the preview
        private readonly IReadOnlyList<(string Name, Func<SKBitmap?> Load)> _sources;
        private SKBitmap? _picture;

        public VanishingPointViewModel(SKBitmap flattened, IReadOnlyList<(string Name, Func<SKBitmap?> Load)> sources)
        {
            DocumentWidth = flattened.Width;
            DocumentHeight = flattened.Height;
            Scale = Math.Min(1f, PreviewMax / (float)Math.Max(DocumentWidth, DocumentHeight));
            _document = flattened.Resize(new SKImageInfo(Math.Max(1, (int)(DocumentWidth * Scale)), Math.Max(1, (int)(DocumentHeight * Scale))),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            _sources = sources;
            SourceNames = sources.Select(s => s.Name).ToList();

            // A floor-like start: narrower at the top.
            float w = DocumentWidth, h = DocumentHeight;
            Corners = new[]
            {
                new SKPoint(w * 0.35f, h * 0.35f), new SKPoint(w * 0.65f, h * 0.35f),
                new SKPoint(w * 0.85f, h * 0.8f), new SKPoint(w * 0.15f, h * 0.8f),
            };
            source = SourceNames.FirstOrDefault() ?? string.Empty;
            LoadPicture();
            Redraw();
        }

        public int DocumentWidth { get; }
        public int DocumentHeight { get; }

        /// <summary>Preview pixels per image pixel.</summary>
        public float Scale { get; }

        public double PreviewWidth => _document.Width;
        public double PreviewHeight => _document.Height;

        /// <summary>The surface's corners in image pixels: top-left, top-right, bottom-right, bottom-left.</summary>
        public SKPoint[] Corners { get; }

        public IReadOnlyList<string> SourceNames { get; }

        [ObservableProperty] private string source;
        [ObservableProperty] private double placeX = 50;
        [ObservableProperty] private double placeY = 50;
        [ObservableProperty] private double size = 40;
        [ObservableProperty] private bool tile;
        [ObservableProperty] private double opacity = 100;
        [ObservableProperty] private bool showGrid = true;
        [ObservableProperty] private BitmapSource? preview;

        /// <summary>The surface's outline and grid lines in preview pixels (for the overlay).</summary>
        [ObservableProperty] private string gridPath = string.Empty;

        public bool HasPicture => _picture is not null;

        partial void OnSourceChanged(string value) { LoadPicture(); Redraw(); }
        partial void OnPlaceXChanged(double value) => Redraw();
        partial void OnPlaceYChanged(double value) => Redraw();
        partial void OnSizeChanged(double value) => Redraw();
        partial void OnTileChanged(bool value) => Redraw();
        partial void OnOpacityChanged(double value) => Redraw();
        partial void OnShowGridChanged(bool value) => Redraw();

        private void LoadPicture()
        {
            _picture?.Dispose();
            _picture = null;
            var entry = _sources.FirstOrDefault(s => s.Name == Source);
            if (entry.Load is not null) _picture = entry.Load();
            OnPropertyChanged(nameof(HasPicture));
        }

        /// <summary>A corner dragged to (<paramref name="x"/>, <paramref name="y"/>) in preview pixels.</summary>
        public void MoveCorner(int index, double x, double y)
        {
            if (index is < 0 or > 3) return;
            Corners[index] = new SKPoint(
                (float)Math.Clamp(x / Scale, -DocumentWidth, DocumentWidth * 2),
                (float)Math.Clamp(y / Scale, -DocumentHeight, DocumentHeight * 2));
            Redraw();
        }

        public SKPoint CornerOnPreview(int index) => new(Corners[index].X * Scale, Corners[index].Y * Scale);

        private SKPoint[] PreviewCorners() => Corners.Select(c => new SKPoint(c.X * Scale, c.Y * Scale)).ToArray();

        private void Redraw()
        {
            var corners = PreviewCorners();
            using var frame = _document.Copy();
            using (var canvas = new SKCanvas(frame))
            {
                if (_picture is not null)
                    VanishingPoint.Draw(canvas, corners, _picture, (float)PlaceX / 100, (float)PlaceY / 100, (float)Size / 100, Tile, (float)Opacity / 100);
            }
            Preview = Timeline.TimelineViewModel.FastThumbnail(frame, Math.Max(frame.Width, frame.Height));
            GridPath = BuildGrid(corners);
            CornersChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Raised after a redraw, so the view moves its corner handles.</summary>
        public event EventHandler? CornersChanged;

        private string BuildGrid(SKPoint[] corners)
        {
            var m = VanishingPoint.SquareToQuad(corners);
            var parts = new List<string>();
            string P(SKPoint p) => FormattableString.Invariant($"{p.X:0.#},{p.Y:0.#}");
            parts.Add($"M{P(corners[0])} L{P(corners[1])} L{P(corners[2])} L{P(corners[3])} Z");
            if (ShowGrid)
                for (var i = 1; i < 8; i++)
                {
                    var t = i / 8f;
                    parts.Add($"M{P(m.MapPoint(new SKPoint(t, 0)))} L{P(m.MapPoint(new SKPoint(t, 1)))}");
                    parts.Add($"M{P(m.MapPoint(new SKPoint(0, t)))} L{P(m.MapPoint(new SKPoint(1, t)))}");
                }
            return string.Join(" ", parts);
        }

        /// <summary>The finished layer at full size, or null without a picture.</summary>
        public SKBitmap? RenderLayer() => _picture is null ? null
            : VanishingPoint.Render(DocumentWidth, DocumentHeight, Corners, _picture,
                (float)PlaceX / 100, (float)PlaceY / 100, (float)Size / 100, Tile, (float)Opacity / 100);

        public void Dispose()
        {
            _picture?.Dispose();
            _document.Dispose();
        }
    }
}
