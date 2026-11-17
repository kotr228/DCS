using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Clipboard;
using JolieCat.Core.Documents;
using JolieCat.UI.Views;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class MainViewModel
    {
        /// <summary>Filter &gt; Vanishing Point...: lays a picture onto a flat surface of the
        /// photo, in perspective, as a new layer.</summary>
        [RelayCommand]
        private void VanishingPoint()
        {
            if (IsSaving || ActiveDocument is null) return;
            using var flat = Core.Rendering.SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);
            using var model = new VanishingPointViewModel(flat, VanishingPointSources());
            var dialog = new VanishingPointDialog(model) { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;
            using var layer = model.RenderLayer();
            if (layer is not null) Layers.AddPictureLayer("Vanishing Point", layer);
        }

        /// <summary>Edit &gt; Transform &gt; Distort / Perspective...: pin the active layer's
        /// painted part by its four corners.</summary>
        [RelayCommand]
        private void DistortLayer()
        {
            if (IsSaving || ActiveDocument is null) return;
            if (Layers.ActiveLayer?.Model is not { IsGroup: false, Adjustment: null } layer || layer.IsText || layer.IsShape || layer.IsFill || layer.SmartObject is not null)
            {
                MessageBox.Show("Pick a pixel layer (text, shape, fill and Smart Object layers need Rasterize first).", "Distort / Perspective",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (SpriteSheetTools.Bounds(layer.Bitmap, new SKRectI(0, 0, layer.Bitmap.Width, layer.Bitmap.Height)) is not { } bounds)
            {
                MessageBox.Show("This layer is empty.", "Distort / Perspective", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // The preview shows the rest of the image under the layer.
            var wasVisible = layer.IsVisible;
            layer.IsVisible = false;
            SKBitmap background;
            try { background = Core.Rendering.SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight); }
            finally { layer.IsVisible = wasVisible; }

            using (background)
            using (var model = new CornerPinViewModel(background, SpriteSheetTools.Cell(layer.Bitmap, bounds), bounds, Layers.DocumentWidth, Layers.DocumentHeight))
            {
                var dialog = new CornerPinDialog(model) { Owner = Application.Current.MainWindow };
                if (dialog.ShowDialog() != true) return;
                using var result = model.Render();
                Layers.ReplaceLayerPixels(layer, result, "Distort");
            }
        }

        /// <summary>What the dialog can lay on the surface.</summary>
        private List<(string Name, Func<SKBitmap?> Load)> VanishingPointSources()
        {
            var sources = new List<(string, Func<SKBitmap?>)>();
            if (PixelClipboard.Content is { } clip) sources.Add(("Copied pixels", () => clip.Copy()));
            foreach (var layer in Layers.Scene.Layers.Where(l => !l.IsGroup && l.Adjustment is null).Reverse())
            {
                var source = layer;
                sources.Add(($"Layer: {layer.Name}", () =>
                    SpriteSheetTools.Bounds(source.Bitmap, new SKRectI(0, 0, source.Bitmap.Width, source.Bitmap.Height)) is { } used
                        ? SpriteSheetTools.Cell(source.Bitmap, used)
                        : null));
            }
            sources.Add(("Open a picture...", OpenPictureForVanishingPoint));
            sources.Add(("Sample: checker tiles", CheckerTile));
            return sources;
        }

        private static SKBitmap? OpenPictureForVanishingPoint()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "A picture to lay on the surface",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*",
            };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true) return null;
            return SKBitmap.Decode(dialog.FileName);
        }

        /// <summary>A black-and-white floor tile (2 x 2 squares), for trying Tile out.</summary>
        private static SKBitmap CheckerTile()
        {
            var tile = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(tile);
            canvas.Clear(new SKColor(235, 232, 225));
            using var dark = new SKPaint { Color = new SKColor(40, 40, 44) };
            canvas.DrawRect(0, 0, 32, 32, dark);
            canvas.DrawRect(32, 32, 32, 32, dark);
            return tile;
        }
    }
}
