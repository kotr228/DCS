using System;
using System.Collections.Generic;
using System.Linq;

namespace JolieCat.Core.Documents
{
    /// <summary>Keeps the guides in place on the image when the canvas is rotated,
    /// flipped, resized, resampled or cropped.</summary>
    public sealed partial class Scene
    {
        /// <summary>Replaces every guide with <paramref name="map"/>'s result; guides that
        /// end up off the new canvas are dropped.</summary>
        private void MapGuides(Func<Guide, Guide> map, int newWidth, int newHeight)
        {
            var mapped = Guides.Select(map)
                .Where(g => g.Position >= 0 && g.Position <= (g.IsVertical ? newWidth : newHeight))
                .ToList();
            Guides.Clear();
            Guides.AddRange(mapped);
        }

        /// <summary>Puts back a saved set of guides (undo/redo).</summary>
        public void RestoreGuides(IEnumerable<Guide> guides)
        {
            var copy = guides.ToList();
            Guides.Clear();
            Guides.AddRange(copy);
        }

        internal void RotateGuides(int turns, int oldWidth, int oldHeight)
        {
            var newWidth = turns % 2 == 1 ? oldHeight : oldWidth;
            var newHeight = turns % 2 == 1 ? oldWidth : oldHeight;
            MapGuides(g => turns switch
            {
                // 90 degrees clockwise: (x, y) -> (H - y, x)
                1 => g.IsVertical ? new Guide(false, g.Position) : new Guide(true, oldHeight - g.Position),
                2 => g with { Position = (g.IsVertical ? oldWidth : oldHeight) - g.Position },
                // 90 degrees counter-clockwise: (x, y) -> (y, W - x)
                3 => g.IsVertical ? new Guide(false, oldWidth - g.Position) : new Guide(true, g.Position),
                _ => g,
            }, newWidth, newHeight);
        }

        internal void FlipGuides(bool horizontal, int width, int height) =>
            MapGuides(g => g.IsVertical == horizontal ? g with { Position = (horizontal ? width : height) - g.Position } : g, width, height);

        internal void ShiftGuides(float dx, float dy, int newWidth, int newHeight) =>
            MapGuides(g => g with { Position = g.Position + (g.IsVertical ? dx : dy) }, newWidth, newHeight);

        internal void ScaleGuides(float sx, float sy, int newWidth, int newHeight) =>
            MapGuides(g => g with { Position = g.Position * (g.IsVertical ? sx : sy) }, newWidth, newHeight);
    }
}
