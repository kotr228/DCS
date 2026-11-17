using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>A named area of the document exported as its own picture (an app icon,
    /// a banner, a phone screen...), like an artboard in Photoshop.</summary>
    public sealed record Artboard(string Name, SKRectI Bounds);

    /// <summary>The document's artboards. They move with the image when the canvas is
    /// rotated, flipped, resized, resampled or cropped, like the guides.</summary>
    public sealed partial class Scene
    {
        public List<Artboard> Artboards { get; } = new();

        /// <summary>Puts back a saved set of artboards (undo/redo).</summary>
        public void RestoreArtboards(IEnumerable<Artboard> artboards)
        {
            var copy = artboards.ToList();
            Artboards.Clear();
            Artboards.AddRange(copy);
        }

        /// <summary>Maps each artboard's corners with <paramref name="map"/>; ones left with
        /// nothing on the new canvas are dropped, the rest are cut to it.</summary>
        private void MapArtboards(Func<SKPoint, SKPoint> map, int newWidth, int newHeight)
        {
            var canvas = new SKRectI(0, 0, newWidth, newHeight);
            var mapped = new List<Artboard>();
            foreach (var a in Artboards)
            {
                var p = map(new SKPoint(a.Bounds.Left, a.Bounds.Top));
                var q = map(new SKPoint(a.Bounds.Right, a.Bounds.Bottom));
                var r = SKRectI.Round(new SKRect(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.X, q.X), Math.Max(p.Y, q.Y)));
                r.Intersect(canvas);
                if (r.Width > 0 && r.Height > 0) mapped.Add(a with { Bounds = r });
            }
            Artboards.Clear();
            Artboards.AddRange(mapped);
        }

        internal void RotateArtboards(int turns, int oldWidth, int oldHeight)
        {
            var newWidth = turns % 2 == 1 ? oldHeight : oldWidth;
            var newHeight = turns % 2 == 1 ? oldWidth : oldHeight;
            MapArtboards(p => turns switch
            {
                1 => new SKPoint(oldHeight - p.Y, p.X),
                2 => new SKPoint(oldWidth - p.X, oldHeight - p.Y),
                3 => new SKPoint(p.Y, oldWidth - p.X),
                _ => p,
            }, newWidth, newHeight);
        }

        internal void FlipArtboards(bool horizontal, int width, int height) =>
            MapArtboards(p => horizontal ? new SKPoint(width - p.X, p.Y) : new SKPoint(p.X, height - p.Y), width, height);

        internal void ShiftArtboards(float dx, float dy, int newWidth, int newHeight) =>
            MapArtboards(p => new SKPoint(p.X + dx, p.Y + dy), newWidth, newHeight);

        internal void ScaleArtboards(float sx, float sy, int newWidth, int newHeight) =>
            MapArtboards(p => new SKPoint(p.X * sx, p.Y * sy), newWidth, newHeight);
    }
}
