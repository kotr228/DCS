using System;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Filters;
using JolieCat.UI.Views;

namespace JolieCat.UI.ViewModels
{
    public partial class MainViewModel
    {
        /// <summary>Image &gt; Mode: "Grayscale", "Indexed" or "Bitmap".</summary>
        [RelayCommand]
        private void ConvertMode(string mode)
        {
            if (IsSaving || ActiveDocument is null) return;
            switch (mode)
            {
                case "Grayscale":
                    Layers.ConvertToGrayscale();
                    break;

                case "Indexed":
                {
                    var options = new FieldsDialogViewModel("Indexed Color",
                        "Every pixel layer is redrawn with one palette made from the whole image: fewer colors, like a GIF.");
                    var colors = options.Add(new SliderDialogField("Colors", 2, 256, 16));
                    var dither = options.Add(new ChoiceDialogField("Dither", new[] { "Diffusion", "None" }));
                    if (!FieldsDialog.Show(options)) return;
                    Layers.ConvertToIndexed((int)Math.Round(colors.Value), dither.Selected == "Diffusion");
                    break;
                }

                case "Bitmap":
                {
                    var options = new FieldsDialogViewModel("Bitmap",
                        "Every pixel layer becomes pure black and white.");
                    var method = options.Add(new ChoiceDialogField("Method", new[] { "Diffusion Dither", "50% Threshold" }));
                    if (!FieldsDialog.Show(options)) return;
                    Layers.ConvertToBitmapMode(method.Selected == "Diffusion Dither");
                    break;
                }
            }
        }

        /// <summary>Layer &gt; Smart Objects: "Convert", "Replace" or "Export".</summary>
        [RelayCommand]
        private void SmartObject(string what)
        {
            if (IsSaving || ActiveDocument is null || Layers.ActiveLayer?.Model is not { } layer) return;
            switch (what)
            {
                case "Convert":
                    if (layer.SmartObject is not null) return;
                    if (!Layers.ConvertToSmartObject())
                        MessageBox.Show("Pick a pixel layer with something painted on it.", "Convert to Smart Object", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;

                case "Replace":
                {
                    if (layer.SmartObject is null) { NotSmart("Replace Contents"); return; }
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Replace Contents: the new picture",
                        Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*",
                    };
                    if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
                    using var picture = SkiaSharp.SKBitmap.Decode(dialog.FileName);
                    if (picture is null)
                    {
                        MessageBox.Show("That picture couldn't be read.", "Replace Contents", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Layers.ReplaceSmartObjectContents(picture, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
                    break;
                }

                case "Export":
                {
                    if (layer.SmartObject is not { } content) { NotSmart("Export Contents"); return; }
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Export Contents",
                        Filter = "PNG Image (*.png)|*.png",
                        DefaultExt = ".png",
                        FileName = layer.Name + ".png",
                    };
                    if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
                    try
                    {
                        using var image = SkiaSharp.SKImage.FromBitmap(content.SourceBitmap);
                        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                        using var file = System.IO.File.Create(dialog.FileName);
                        data.SaveTo(file);
                    }
                    catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                    {
                        MessageBox.Show(ex.Message, "Export Contents", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    break;
                }
            }

            static void NotSmart(string title) =>
                MessageBox.Show("Pick a Smart Object layer (Layer > Smart Objects > Convert to Smart Object, or File > Place Smart Object).", title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Layer &gt; Type on Path...: text that follows the Pen tool's path.</summary>
        [RelayCommand]
        private void TypeOnPath()
        {
            if (IsSaving || ActiveDocument is null) return;
            if (Canvas.PenPath is null)
            {
                MessageBox.Show("Draw a path with the Pen tool first (click points; drag to make curves), then choose Type on Path.",
                    "Type on Path", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var options = new FieldsDialogViewModel("Type on Path",
                "The text follows the Pen path, in the Text tool's font and size and the foreground color. It stays editable with the Text tool.");
            var text = options.Add(new TextDialogField("Text", "Type on a path"));
            if (!FieldsDialog.Show(options)) return;
            Canvas.TypeOnPenPath(text.Text);
        }

        /// <summary>Filter &gt; Other &gt; Custom...: your own convolution kernel, previewed live.</summary>
        [RelayCommand]
        private void CustomFilter()
        {
            if (IsSaving || Layers.ActiveLayer?.Model is not { IsGroup: false }) return;
            var options = new FieldsDialogViewModel("Custom Filter",
                "Each pixel becomes its neighbours times these numbers (rows split by ;, the middle number is the pixel itself; 3x3, 5x5 or 7x7), divided by Scale, plus Offset. Sharpen: 0 -1 0; -1 5 -1; 0 -1 0. Emboss: -2 -1 0; -1 1 1; 0 1 2.");
            var kernel = options.Add(new TextDialogField("Kernel", "0 -1 0; -1 5 -1; 0 -1 0"));
            var scale = options.Add(new SliderDialogField("Scale", 1, 100, 1));
            var offset = options.Add(new SliderDialogField("Offset", -255, 255, 0));
            Canvas.BeginAdjustment("Custom Filter");
            void Preview()
            {
                if (MoreFilters2.ParseKernel(kernel.Text) is { } k)
                    Canvas.UpdateAdjustmentPreview(src => MoreFilters2.Custom(src, k, (float)scale.Value, (float)offset.Value));
            }
            options.Changed += Preview;
            Preview();
            if (FieldsDialog.Show(options) && MoreFilters2.ParseKernel(kernel.Text) is not null) Canvas.CommitAdjustment();
            else Canvas.CancelAdjustment();
        }

        /// <summary>Filter &gt; Distort &gt; Displace...: shifts pixels by another layer's (or a
        /// file's) brightness - text that follows the folds of cloth, ripples on water.</summary>
        [RelayCommand]
        private void DisplaceFilter()
        {
            if (IsSaving || Layers.ActiveLayer?.Model is not { IsGroup: false } target) return;
            var maps = Layers.Scene.Layers.Where(l => !l.IsGroup && l.Adjustment is null && !ReferenceEquals(l, target)).Reverse().ToList();
            var names = maps.Select(l => "Layer: " + l.Name).Append("Open a picture...").ToList();
            var options = new FieldsDialogViewModel("Displace",
                "The map's brightness moves the pixels: middle gray stays, lighter moves right / down, darker left / up. A blurred, gray copy of the photo makes a good map.");
            var source = options.Add(new ChoiceDialogField("Map", names));
            var horizontal = options.Add(new SliderDialogField("Horizontal Scale", -100, 100, 10, "px"));
            var vertical = options.Add(new SliderDialogField("Vertical Scale", -100, 100, 10, "px"));
            if (!FieldsDialog.Show(options)) return;

            SkiaSharp.SKBitmap? map;
            var index = names.IndexOf(source.Selected);
            if (index >= 0 && index < maps.Count)
            {
                // The map as it looks (its gray), not just its own pixels' alpha.
                map = maps[index].Bitmap.Copy();
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Displacement map", Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*" };
                if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
                map = SkiaSharp.SKBitmap.Decode(dialog.FileName);
                if (map is null) return;
            }
            using (map)
            {
                Canvas.BeginAdjustment("Displace");
                Canvas.UpdateAdjustmentPreview(src => MoreFilters2.Displace(src, map, (float)horizontal.Value, (float)vertical.Value));
                Canvas.CommitAdjustment();
            }
        }

        /// <summary>Edit &gt; Define Brush Preset...: the selected area (or the whole visible
        /// image) as a picture brush tip - dark paints, light doesn't.</summary>
        [RelayCommand]
        private void DefineBrushPreset()
        {
            if (IsSaving || ActiveDocument is null) return;
            using var flat = Core.Rendering.SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);
            var selection = Layers.Scene.Selection;
            using var area = selection.HasSelection && selection.Region is { } region
                ? CutSelection(flat, region)
                : flat.Copy();
            var tip = Media.BrushLibrary.MakeTip(area);
            if (tip is null)
            {
                MessageBox.Show("Nothing dark there to make a brush from: the tip is made of the darker, opaque parts (white and see-through parts don't paint).",
                    "Define Brush Preset", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var options = new FieldsDialogViewModel("Define Brush Preset", $"{tip.Width} x {tip.Height} px tip. It's kept with your brush presets.");
            var n = 1;
            while (Media.BrushLibrary.Shared.Find($"Brush {n}") is not null) n++;
            var name = options.Add(new TextDialogField("Name", $"Brush {n}"));
            if (!FieldsDialog.Show(options) || string.IsNullOrWhiteSpace(name.Text)) { tip.Dispose(); return; }

            var preset = Media.BrushLibrary.Shared.SaveTip(name.Text.Trim(), tip);
            // Ready to paint with: the Brush tool takes the new tip.
            Toolbox.SelectTool(Shared.Enums.ToolType.Brush);
            if (Toolbox.CurrentToolOptions is ToolOptions.PaintToolOptionsViewModel paint)
                paint.LoadPresetCommand.Execute(preset.Name);
            tip.Dispose();
        }

        /// <summary>The pixels of <paramref name="image"/> inside <paramref name="region"/>, cut to its bounds.</summary>
        private static SkiaSharp.SKBitmap CutSelection(SkiaSharp.SKBitmap image, SkiaSharp.SKRegion region)
        {
            var b = region.Bounds;
            var result = new SkiaSharp.SKBitmap(Math.Max(1, b.Width), Math.Max(1, b.Height), SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
            using var canvas = new SkiaSharp.SKCanvas(result);
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            canvas.Translate(-b.Left, -b.Top);
            using var clip = region.GetBoundaryPath();
            canvas.ClipPath(clip);
            canvas.DrawBitmap(image, 0, 0);
            return result;
        }

        /// <summary>Layer &gt; Layer Mask from Transparency.</summary>
        [RelayCommand]
        private void MaskFromTransparency()
        {
            if (IsSaving || ActiveDocument is null) return;
            if (!Layers.AddMaskFromTransparency())
                MessageBox.Show("Pick a pixel layer.", "Layer Mask from Transparency", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Image &gt; Calculations...: two channels blended into a gray mask, used as a
        /// new layer, a selection or the active layer's mask.</summary>
        [RelayCommand]
        private void Calculations()
        {
            if (IsSaving || ActiveDocument is null) return;
            var sources = Layers.Scene.Layers.Where(l => !l.IsGroup && l.Adjustment is null).Reverse().ToList();
            var names = new[] { "Merged" }.Concat(sources.Select(l => l.Name)).ToList();
            var channels = new[] { "Gray", "Red", "Green", "Blue", "Transparency" };
            var options = new FieldsDialogViewModel("Calculations",
                "Blends one channel with another into a gray mask. Light parts select / show, dark parts don't.");
            var source1 = options.Add(new ChoiceDialogField("Source 1", names));
            var channel1 = options.Add(new ChoiceDialogField("Channel 1", channels));
            var invert1 = options.Add(new ChoiceDialogField("Invert 1", new[] { "No", "Yes" }));
            var source2 = options.Add(new ChoiceDialogField("Source 2", names));
            var channel2 = options.Add(new ChoiceDialogField("Channel 2", channels, "Blue"));
            var invert2 = options.Add(new ChoiceDialogField("Invert 2", new[] { "No", "Yes" }));
            var blend = options.Add(new ChoiceDialogField("Blending", Enum.GetNames<ColorModes.ApplyBlend>(), nameof(ColorModes.ApplyBlend.Multiply)));
            var opacity = options.Add(new SliderDialogField("Opacity", 0, 100, 100, "%"));
            var result = options.Add(new ChoiceDialogField("Result", new[] { "New Layer", "Selection", "Layer Mask" }));
            if (!FieldsDialog.Show(options)) return;

            SkiaSharp.SKBitmap Picture(string name)
            {
                var index = names.IndexOf(name);
                return index <= 0
                    ? Core.Rendering.SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight)
                    : sources[index - 1].Bitmap.Copy();
            }
            using var first = Picture(source1.Selected);
            using var second = Picture(source2.Selected);
            using var gray = ColorModes.Calculate(first, Array.IndexOf(channels, channel1.Selected), invert1.Selected == "Yes",
                second, Array.IndexOf(channels, channel2.Selected), invert2.Selected == "Yes",
                Enum.Parse<ColorModes.ApplyBlend>(blend.Selected), opacity.Value);
            Layers.UseGrayResult(gray, result.Selected);
        }

        /// <summary>Image &gt; Apply Image...</summary>
        [RelayCommand]
        private void ApplyImage()
        {
            if (IsSaving || ActiveDocument is null) return;
            if (Layers.ActiveLayer?.Model is not { IsGroup: false, Adjustment: null } target)
            {
                MessageBox.Show("Pick a pixel layer to apply the image to.", "Apply Image", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sources = Layers.Scene.Layers.Where(l => !l.IsGroup && l.Adjustment is null).Reverse().ToList();
            var options = new FieldsDialogViewModel("Apply Image", $"Blends an image onto \"{target.Name}\".");
            var layer = options.Add(new ChoiceDialogField("Source", new[] { "Merged" }.Concat(sources.Select(l => l.Name)).ToList()));
            var channel = options.Add(new ChoiceDialogField("Channel", new[] { "RGB", "Red", "Green", "Blue" }));
            var invert = options.Add(new ChoiceDialogField("Invert", new[] { "No", "Yes" }));
            var blend = options.Add(new ChoiceDialogField("Blending", Enum.GetNames<ColorModes.ApplyBlend>(), nameof(ColorModes.ApplyBlend.Multiply)));
            var opacity = options.Add(new SliderDialogField("Opacity", 0, 100, 100, "%"));
            if (!FieldsDialog.Show(options)) return;

            var index = Array.IndexOf(new[] { "Merged" }.Concat(sources.Select(l => l.Name)).ToArray(), layer.Selected);
            var source = index <= 0 ? null : sources[index - 1];
            Layers.ApplyImage(source, Array.IndexOf(new[] { "RGB", "Red", "Green", "Blue" }, channel.Selected),
                invert.Selected == "Yes", Enum.Parse<ColorModes.ApplyBlend>(blend.Selected), opacity.Value);
        }
    }
}
