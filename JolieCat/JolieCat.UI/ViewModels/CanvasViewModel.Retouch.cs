using System.Linq;
using JolieCat.Core.Filters;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>Smudge, History Brush, Background Eraser, Magic Eraser and Red Eye.</summary>
    public partial class CanvasViewModel
    {
        /// <summary>Where the previous retouch dab of this stroke was (Smudge drags from there).</summary>
        private SKPoint? _lastDabCenter;

        /// <summary>The active layer as it was in the History Brush's source state.</summary>
        private SKBitmap? _historySource;

        /// <summary>The Mixer Brush's paint, as it mixes with the canvas during a stroke.</summary>
        private (float R, float G, float B)? _mixerPaint;

        /// <summary>The layer as it was when the Mixer Brush stroke began: the brush picks
        /// up the canvas's colors, not the paint it has just laid down itself.</summary>
        private SKBitmap? _mixerCanvas;

        /// <summary>Mixer Brush dab: the brush's paint picks up some of the canvas color
        /// under it (by Wet), and the dab is painted with that mix.</summary>
        private SKBitmap MixerPatch(SKBitmap layerBitmap, SKRectI area, SKPoint center, float radius)
        {
            var source = _mixerCanvas ?? layerBitmap;
            var wet = (float)System.Math.Clamp(((_toolbox.CurrentToolOptions as PaintToolOptionsViewModel)?.Wet ?? 50) / 100, 0, 1);
            var brush = BrushColor;
            var paint = _mixerPaint ?? (brush.Red, brush.Green, brush.Blue);

            // Average color under the middle of the brush.
            var r = System.Math.Max(1, (int)(radius * 0.5f));
            double sr = 0, sg = 0, sb = 0, sw = 0;
            for (var y = (int)center.Y - r; y <= (int)center.Y + r; y += System.Math.Max(1, r / 4))
                for (var x = (int)center.X - r; x <= (int)center.X + r; x += System.Math.Max(1, r / 4))
                {
                    if (x < 0 || y < 0 || x >= source.Width || y >= source.Height) continue;
                    var c = source.GetPixel(x, y);
                    var a = c.Alpha / 255.0;
                    sr += c.Red * a; sg += c.Green * a; sb += c.Blue * a; sw += a;
                }
            if (sw > 0)
            {
                var pick = wet * 0.12f; // per dab: a full stroke fades into the canvas color gradually
                paint = (paint.R + ((float)(sr / sw) - paint.R) * pick,
                         paint.G + ((float)(sg / sw) - paint.G) * pick,
                         paint.B + ((float)(sb / sw) - paint.B) * pick);
            }
            _mixerPaint = paint;

            var patch = new SKBitmap(area.Width, area.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            patch.Erase(new SKColor((byte)paint.R, (byte)paint.G, (byte)paint.B));
            return patch;
        }

        /// <summary>Prepares a retouching stroke; false (with a hint) if it can't paint.</summary>
        private bool BeginRetouchStroke(Core.Documents.Layer layer)
        {
            _lastDabCenter = null;
            _mixerPaint = null;
            _mixerCanvas?.Dispose();
            _mixerCanvas = _toolbox.ActiveTool.Type == ToolType.MixerBrush ? layer.PaintBitmap.Copy() : null;
            if (_toolbox.ActiveTool.Type != ToolType.HistoryBrush) return true;

            _historySource?.Dispose();
            _historySource = null;
            var source = Layers.HistoryBrushSource;
            var saved = source?.Layers.FirstOrDefault(l => l.Id == layer.Id);
            if (layer.PaintsMask)
            {
                ToolHintText = "The History Brush paints layer pixels, not masks";
                return false;
            }
            if (source is null || saved is null || saved.Width != layer.Bitmap.Width || saved.Height != layer.Bitmap.Height)
            {
                ToolHintText = source is null
                    ? "Pick a History Brush source in the History panel's snapshots"
                    : $"This layer isn't in \"{source.Name}\" (or its size changed) - pick another source";
                return false;
            }

            _historySource = new SKBitmap(saved.Width, saved.Height, SKColorType.Rgba8888, SKAlphaType.Premul) { Pixels = saved.Pixels };
            ToolHintText = $"Painting from \"{source.Name}\"";
            return true;
        }

        private void EndRetouchStroke()
        {
            _lastDabCenter = null;
            _mixerCanvas?.Dispose();
            _mixerCanvas = null;
            _historySource?.Dispose();
            _historySource = null;
        }

        /// <summary>Magic Eraser: erases the area of similar color around the click
        /// (or that color everywhere, without Contiguous).</summary>
        private void MagicErase(Core.Documents.Layer? layer, SKPoint doc)
        {
            if (layer is null || layer.IsLocked || layer.IsGroup || layer.IsAdjustment || layer.PaintsMask) return;
            var before = layer.PaintBitmap.Pixels;
            FloodFill(layer, doc, SKColors.Transparent);
            Layers.History.Push(PixelEdit(layer, before));
            RaiseInvalidate();
        }

        /// <summary>Red Eye: removes the red from the pupil under the click.</summary>
        private void FixRedEye(Core.Documents.Layer? layer, SKPoint doc)
        {
            if (layer is null || layer.IsLocked || layer.IsGroup || layer.IsAdjustment || layer.PaintsMask) return;
            var options = _toolbox.CurrentToolOptions as RedEyeToolOptionsViewModel;
            var bitmap = layer.PaintBitmap;
            var before = bitmap.Pixels;
            var pixels = bitmap.Pixels;
            var changed = RetouchOps.RemoveRedEye(pixels, bitmap.Width, bitmap.Height, doc,
                (float)(options?.PupilSize ?? 30), (float)((options?.DarkenAmount ?? 50) / 100));
            if (changed == 0)
            {
                ToolHintText = "No red found there - click on the red of the pupil, or raise Pupil Size";
                return;
            }

            // Only inside the selection, if there is one.
            var selection = Layers.Scene.Selection;
            if (selection.HasSelection)
                for (var i = 0; i < pixels.Length; i++)
                    if (!selection.Contains(i % bitmap.Width, i / bitmap.Width)) pixels[i] = before[i];

            bitmap.Pixels = pixels;
            Layers.History.Push(PixelEdit(layer, before));
            ToolHintText = string.Empty;
            RaiseInvalidate();
        }
    }
}
