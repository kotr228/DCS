using System;
using System.Linq;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Edit &gt; Transform &gt; Distort / Perspective: the layer's painted part pinned by its four
    /// corners, each dragged on a preview. In Perspective mode, dragging a corner along an
    /// edge moves the corner across from it the opposite way, like Photoshop's Perspective.
    /// </summary>
    public sealed partial class CornerPinViewModel : ObservableObject, IDisposable
    {
        public const int PreviewMax = 760;

        private readonly SKBitmap _background;   // the image without the layer, small
        private readonly SKBitmap _content;      // the layer's painted part, full size
        private readonly SKPoint[] _start;

        public CornerPinViewModel(SKBitmap backgroundFull, SKBitmap content, SKRectI bounds, int documentWidth, int documentHeight)
        {
            DocumentWidth = documentWidth;
            DocumentHeight = documentHeight;
            Scale = Math.Min(1f, PreviewMax / (float)Math.Max(documentWidth, documentHeight));
            _background = backgroundFull.Resize(new SKImageInfo(Math.Max(1, (int)(documentWidth * Scale)), Math.Max(1, (int)(documentHeight * Scale))),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            _content = content;
            Corners = new[]
            {
                new SKPoint(bounds.Left, bounds.Top), new SKPoint(bounds.Right, bounds.Top),
                new SKPoint(bounds.Right, bounds.Bottom), new SKPoint(bounds.Left, bounds.Bottom),
            };
            _start = Corners.ToArray();
            Redraw();
        }

        public int DocumentWidth { get; }
        public int DocumentHeight { get; }
        public float Scale { get; }
        public double PreviewWidth => _background.Width;
        public double PreviewHeight => _background.Height;

        /// <summary>Top-left, top-right, bottom-right, bottom-left, in image pixels.</summary>
        public SKPoint[] Corners { get; }

        [ObservableProperty] private bool perspective;
        [ObservableProperty] private BitmapSource? preview;
        [ObservableProperty] private string outline = string.Empty;

        public event EventHandler? CornersChanged;

        public SKPoint CornerOnPreview(int i) => new(Corners[i].X * Scale, Corners[i].Y * Scale);

        /// <summary>Corner <paramref name="index"/> dragged by (<paramref name="dx"/>, <paramref name="dy"/>) preview pixels.</summary>
        public void DragCorner(int index, double dx, double dy)
        {
            if (index is < 0 or > 3) return;
            var d = new SKPoint((float)(dx / Scale), (float)(dy / Scale));
            if (!Perspective)
            {
                Corners[index] += d;
            }
            else
            {
                // Mostly sideways: the corner on the same top/bottom edge moves the other way;
                // mostly up/down: the one on the same left/right edge does.
                if (Math.Abs(d.X) >= Math.Abs(d.Y))
                {
                    var partner = index switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
                    Corners[index] += new SKPoint(d.X, 0);
                    Corners[partner] -= new SKPoint(d.X, 0);
                }
                else
                {
                    var partner = index switch { 0 => 3, 3 => 0, 1 => 2, _ => 1 };
                    Corners[index] += new SKPoint(0, d.Y);
                    Corners[partner] -= new SKPoint(0, d.Y);
                }
            }
            Redraw();
        }

        public void Reset()
        {
            for (var i = 0; i < 4; i++) Corners[i] = _start[i];
            Redraw();
        }

        /// <summary>Draws the content pinned to <paramref name="corners"/>.</summary>
        private void DrawPinned(SKCanvas canvas, SKPoint[] corners)
        {
            canvas.Save();
            canvas.Concat(VanishingPoint.SquareToQuad(corners));
            canvas.Scale(1f / _content.Width, 1f / _content.Height);
            using var image = SKImage.FromBitmap(_content);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
            canvas.Restore();
        }

        private void Redraw()
        {
            var small = Corners.Select(c => new SKPoint(c.X * Scale, c.Y * Scale)).ToArray();
            using var frame = _background.Copy();
            using (var canvas = new SKCanvas(frame)) DrawPinned(canvas, small);
            Preview = Timeline.TimelineViewModel.FastThumbnail(frame, Math.Max(frame.Width, frame.Height));
            string P(SKPoint p) => FormattableString.Invariant($"{p.X:0.#},{p.Y:0.#}");
            Outline = $"M{P(small[0])} L{P(small[1])} L{P(small[2])} L{P(small[3])} Z";
            CornersChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The new layer pixels, full size.</summary>
        public SKBitmap Render()
        {
            var result = new SKBitmap(DocumentWidth, DocumentHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            DrawPinned(canvas, Corners);
            return result;
        }

        public void Dispose()
        {
            _background.Dispose();
            _content.Dispose();
        }
    }
}
