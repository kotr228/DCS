using System.Linq;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Quick Mask (Q): the selection becomes a mask you paint - the unselected area
    /// shows red; painting black adds to the red (deselects), white removes it (selects),
    /// grays select partly. Q again turns the mask back into a selection. Every brush,
    /// eraser, fill and gradient works on it, and each stroke is an undo step.
    /// </summary>
    public partial class CanvasViewModel
    {
        private LayerMask? _quickMask;

        public bool IsQuickMask => _quickMask is not null;

        /// <summary>The mask being painted (white = selected), for the red overlay.</summary>
        public SKBitmap? QuickMaskBitmap => _quickMask?.Bitmap;

        [RelayCommand]
        public void ToggleQuickMask()
        {
            if (IsQuickMask) ExitQuickMask();
            else EnterQuickMask();
        }

        private void EnterQuickMask()
        {
            int width = Layers.DocumentWidth, height = Layers.DocumentHeight;
            var mask = new LayerMask(width, height);
            var selection = Layers.Scene.Selection;
            if (selection.HasSelection && selection.Path is { } path)
            {
                mask.Canvas.Clear(SKColors.Black);
                using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
                if (selection.Feather > 0)
                    paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, selection.Feather / 2);
                mask.Canvas.DrawPath(path, paint);
            }
            else
            {
                // Nothing selected: start clear, so painting black marks what to leave out.
                mask.Canvas.Clear(SKColors.White);
            }

            _quickMask = mask;
            foreach (var layer in Layers.Scene.Layers) layer.PaintRedirect = mask;
            selection.Clear();
            ToolHintText = "Quick Mask: paint black to hide (red), white to select. Press Q to turn it into a selection.";
            OnPropertyChanged(nameof(IsQuickMask));
            RaiseInvalidate();
        }

        private void ExitQuickMask()
        {
            var mask = _quickMask!;
            _quickMask = null;
            foreach (var layer in Layers.Scene.Layers) layer.PaintRedirect = null;

            // Back to a selection: the parts painted at least half white.
            var pixels = mask.Bitmap.Pixels;
            static bool Selected(SKColor c) => (0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue) * c.Alpha / 255 >= 128;
            var path = SelectionOps.FromMask(mask.Bitmap.Width, mask.Bitmap.Height, i => Selected(pixels[i]));
            var selection = Layers.Scene.Selection;
            var everything = path is not null && pixels.All(Selected);
            if (path is null || everything) selection.Clear();
            else selection.SetPath(path, Layers.DocumentWidth, Layers.DocumentHeight);

            // The mask itself is kept alive: undo steps painted on it still point to it.
            ToolHintText = string.Empty;
            OnPropertyChanged(nameof(IsQuickMask));
            RaiseInvalidate();
        }
    }
}
