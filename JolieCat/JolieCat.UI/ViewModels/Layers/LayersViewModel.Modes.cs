using System;
using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Documents;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>Image &gt; Mode conversions and Image &gt; Apply Image.</summary>
    public partial class LayersViewModel
    {
        /// <summary>The layers a mode conversion repaints: plain pixel layers. Text, shape,
        /// fill, adjustment and Smart Object layers keep their own settings and are left
        /// out (except Grayscale, which also grays text color).</summary>
        private List<Layer> PixelLayers() => Scene.Layers
            .Where(l => !l.IsGroup && l.Adjustment is null && l.SmartObject is null && l.Fill is null && l.Shape is null && l.Text is null)
            .ToList();

        /// <summary>Repaints each of <paramref name="layers"/> with <paramref name="convert"/>. One undo step.</summary>
        private void ConvertLayers(IReadOnlyList<Layer> layers, Func<SKBitmap, SKBitmap> convert, string stepName, Action? alsoDo = null)
        {
            if (layers.Count == 0 && alsoDo is null) return;
            History.PushStructural(CaptureStructuralChange(() =>
            {
                foreach (var layer in layers)
                {
                    using var converted = convert(layer.Bitmap);
                    using var canvas = new SKCanvas(layer.Bitmap);
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawBitmap(converted, 0, 0);
                }
                alsoDo?.Invoke();
            }), stepName);
            NotifyContentChanged();
        }

        /// <summary>Image &gt; Mode &gt; Grayscale: every pixel layer (and text color) to gray.</summary>
        public void ConvertToGrayscale()
        {
            var texts = Scene.Layers.Where(l => l.Text is not null).ToList();
            ConvertLayers(PixelLayers(), ColorModes.Grayscale, "Grayscale", () =>
            {
                foreach (var layer in texts)
                {
                    var text = layer.Text!;
                    var c = text.Color;
                    var y = (byte)Math.Round(0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue);
                    layer.Text = text with { Color = new SKColor(y, y, y, c.Alpha) };
                    using var canvas = new SKCanvas(layer.Bitmap);
                    canvas.Clear(SKColors.Transparent);
                    layer.Text.Render(canvas);
                }
            });
        }

        /// <summary>Image &gt; Mode &gt; Indexed Color: one palette of <paramref name="colors"/>
        /// colors made from the whole visible image, used on every pixel layer.</summary>
        public void ConvertToIndexed(int colors, bool dither)
        {
            using var flat = Core.Rendering.SceneCompositor.Flatten(Scene, DocumentWidth, DocumentHeight);
            var palette = ColorModes.Palette(flat, colors);
            ConvertLayers(PixelLayers(), b => ColorModes.Indexed(b, palette, dither), "Indexed Color");
        }

        /// <summary>Image &gt; Mode &gt; Bitmap: every pixel layer to pure black and white.</summary>
        public void ConvertToBitmapMode(bool dither) =>
            ConvertLayers(PixelLayers(), b => ColorModes.Bitmap(b, dither), "Bitmap");

        /// <summary>Replaces <paramref name="layer"/>'s pixels with <paramref name="picture"/>
        /// (document-sized). One undo step named <paramref name="stepName"/>.</summary>
        public void ReplaceLayerPixels(Layer layer, SKBitmap picture, string stepName) =>
            ConvertLayers(new[] { layer }, _ => picture.Copy(), stepName);

        /// <summary>Layer &gt; Smart Objects &gt; Convert to Smart Object: the active pixel layer's
        /// painted part becomes a Smart Object in the same place (later transforms then
        /// resample the original, without losing quality). One undo step.</summary>
        public bool ConvertToSmartObject()
        {
            if (ActiveLayer?.Model is not { IsGroup: false, Adjustment: null, SmartObject: null } layer) return false;
            if (SpriteSheetTools.Bounds(layer.Bitmap, new SKRectI(0, 0, layer.Bitmap.Width, layer.Bitmap.Height)) is not { } bounds) return false;
            History.PushStructural(CaptureStructuralChange(() =>
            {
                var content = SpriteSheetTools.Cell(layer.Bitmap, bounds);
                var embedded = new Scene(layer.Name);
                embedded.AddLayer("Content", content.Width, content.Height).Canvas.DrawBitmap(content, 0, 0);
                var smart = Layer.CreateSmartObject(layer.Name, DocumentWidth, DocumentHeight, content, embedded,
                    SKMatrix.CreateTranslation(bounds.Left, bounds.Top));
                smart.Opacity = layer.Opacity;
                smart.BlendMode = layer.BlendMode;
                smart.IsVisible = layer.IsVisible;
                smart.ParentId = layer.ParentId;
                var index = Scene.IndexOf(layer);
                Scene.RemoveLayer(layer);
                Scene.InsertLayer(index, smart);
                Scene.ActiveLayer = smart;
            }), "Convert to Smart Object");
            return true;
        }

        /// <summary>Layer &gt; Smart Objects &gt; Replace Contents: a new picture in the active
        /// Smart Object, in the same box. One undo step.</summary>
        public bool ReplaceSmartObjectContents(SKBitmap picture, string name)
        {
            if (ActiveLayer?.Model is not { SmartObject: not null } layer) return false;
            History.PushStructural(CaptureStructuralChange(() =>
            {
                var embedded = new Scene(name);
                embedded.AddLayer("Content", picture.Width, picture.Height).Canvas.DrawBitmap(picture, 0, 0);
                layer.ReplaceSmartObjectContent(picture.Copy(), embedded);
            }), "Replace Contents");
            NotifyContentChanged();
            return true;
        }

        /// <summary>Layer &gt; Layer Mask &gt; From Transparency: a mask that shows the active
        /// layer where it's painted and hides it where it's see-through. One undo step.</summary>
        public bool AddMaskFromTransparency()
        {
            if (ActiveLayer?.Model is not { IsGroup: false, Adjustment: null } layer) return false;
            History.PushStructural(CaptureStructuralChange(() =>
            {
                var mask = layer.Mask ?? layer.AddMask();
                using var gray = ColorModes.Channel(layer.Bitmap, 4, false);
                mask.Canvas.Clear(SKColors.Black);
                mask.Canvas.DrawBitmap(gray, 0, 0);
                mask.IsEnabled = true;
            }), "Layer Mask from Transparency");
            ActiveLayer?.RefreshMaskThumbnail();
            return true;
        }

        /// <summary>Puts a gray picture to use: "New Layer" (a gray layer on top), "Selection"
        /// (the lighter half selected) or "Layer Mask" (on the active layer). One undo step.</summary>
        public void UseGrayResult(SKBitmap gray, string result)
        {
            switch (result)
            {
                case "Selection":
                {
                    var pixels = gray.Pixels;
                    var path = SelectionOps.FromMask(gray.Width, gray.Height, i => pixels[i].Red >= 128);
                    if (path is null) Scene.Selection.Clear();
                    else Scene.Selection.SetPath(path, DocumentWidth, DocumentHeight);
                    NotifyContentChanged();
                    break;
                }
                case "Layer Mask":
                {
                    if (ActiveLayer?.Model is not { } layer) return;
                    History.PushStructural(CaptureStructuralChange(() =>
                    {
                        var mask = layer.Mask ?? layer.AddMask();
                        mask.Canvas.Clear(SKColors.Black);
                        mask.Canvas.DrawBitmap(gray, 0, 0);
                        mask.IsEnabled = true;
                    }), "Calculations");
                    ActiveLayer?.RefreshMaskThumbnail();
                    break;
                }
                default:
                    AddPictureLayer("Calculations", gray);
                    break;
            }
        }

        /// <summary>A new layer named <paramref name="name"/> with <paramref name="picture"/>
        /// (document-sized) on it, above the active layer and made active. One undo step.</summary>
        public void AddPictureLayer(string name, SKBitmap picture)
        {
            History.PushStructural(CaptureStructuralChange(() =>
            {
                var layer = Scene.AddLayer(name, DocumentWidth, DocumentHeight);
                layer.Canvas.DrawBitmap(picture, 0, 0);
                Scene.ActiveLayer = layer;
            }), name);
        }

        /// <summary>Image &gt; Apply Image: blends a layer (or the merged image, <paramref name="sourceLayer"/>
        /// null) - all of it or one channel - onto the active layer. One undo step.</summary>
        public bool ApplyImage(Layer? sourceLayer, int channel, bool invert, ColorModes.ApplyBlend blend, double opacity)
        {
            if (ActiveLayer?.Model is not { } target || target.IsGroup || target.Adjustment is not null) return false;
            using var source = sourceLayer is null
                ? Core.Rendering.SceneCompositor.Flatten(Scene, DocumentWidth, DocumentHeight)
                : sourceLayer.Bitmap.Copy();
            ConvertLayers(new[] { target }, b => ColorModes.ApplyImage(b, source, channel, invert, blend, opacity), "Apply Image");
            return true;
        }
    }
}
