using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using JolieCat.UI.Views;
using Microsoft.Win32;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>File &gt; Export As: the flattened image in one format at several sizes
    /// in one go (e.g. 100%, 50% and 2x for web and retina screens).</summary>
    public partial class MainViewModel
    {
        [RelayCommand]
        private void ExportAs()
        {
            if (IsSaving || ActiveDocument is null) return;

            var options = new FieldsDialogViewModel("Export As",
                "Sizes: a comma-separated list such as \"100%, 50%, 2x\" - one file each (\"@2x\" style suffixes for the extra sizes).");
            var format = options.Add(new ChoiceDialogField("Format", new[] { "PNG", "JPEG", "WebP" }));
            var quality = options.Add(new SliderDialogField("Quality", 1, 100, 90, "%"));
            var sizes = options.Add(new TextDialogField("Sizes", "100%"));
            var trim = options.Add(new ChoiceDialogField("Canvas", new[] { "Whole canvas", "Trim transparent edges" }));
            var name = options.Add(new TextDialogField("File name", Path.GetFileNameWithoutExtension(ActiveDocument.Title)));
            options.Validate = () => ParseScales(sizes.Text).Count == 0 ? "Enter at least one size, such as 100% or 2x." : null;
            if (!FieldsDialog.Show(options)) return;

            var folderDialog = new OpenFolderDialog { Title = "Choose where to save the images" };
            if (folderDialog.ShowDialog(Application.Current.MainWindow) != true) return;

            var written = ExportScaled(folderDialog.FolderName, name.Text, format.Selected, (int)quality.Value,
                ParseScales(sizes.Text), trim.Selected.StartsWith("Trim", StringComparison.Ordinal));
            MessageBox.Show($"Saved {written.Count} file(s) to {folderDialog.FolderName}.", "Export As",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>"100%, 50%, 2x" -> 1, 0.5, 2 (duplicates and nonsense dropped).</summary>
        internal static List<double> ParseScales(string text)
        {
            var scales = new List<double>();
            foreach (var part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim().ToLowerInvariant();
                double value;
                if (t.EndsWith('%') && double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out value)) value /= 100;
                else if (t.EndsWith('x') && double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out value)) { }
                else continue;
                if (value is > 0.001 and <= 16 && !scales.Contains(value)) scales.Add(value);
            }
            return scales;
        }

        /// <summary>Writes the flattened image once per scale; returns the files written.</summary>
        internal List<string> ExportScaled(string folder, string baseName, string format, int quality, IReadOnlyList<double> scales, bool trim)
        {
            Directory.CreateDirectory(folder);
            var (extension, skFormat) = format switch
            {
                "JPEG" => (".jpg", SKEncodedImageFormat.Jpeg),
                "WebP" => (".webp", SKEncodedImageFormat.Webp),
                _ => (".png", SKEncodedImageFormat.Png),
            };

            using var flat = SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);
            var area = trim ? SelectionOps.TrimBounds(flat, null) : new SKRectI(0, 0, flat.Width, flat.Height);
            if (area.IsEmpty) area = new SKRectI(0, 0, flat.Width, flat.Height);

            var written = new List<string>();
            var safe = SafeFileName(string.IsNullOrWhiteSpace(baseName) ? "image" : baseName);
            foreach (var scale in scales)
            {
                var w = Math.Max(1, (int)Math.Round(area.Width * scale));
                var h = Math.Max(1, (int)Math.Round(area.Height * scale));
                using var output = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(output))
                using (var image = SKImage.FromBitmap(flat))
                using (var paint = new SKPaint { IsAntialias = true })
                {
                    canvas.Clear(skFormat == SKEncodedImageFormat.Jpeg ? SKColors.White : SKColors.Transparent);
                    canvas.DrawImage(image, SKRect.Create(area.Left, area.Top, area.Width, area.Height), new SKRect(0, 0, w, h),
                        new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
                }

                var suffix = scale == 1 ? "" : "@" + (scale >= 1 ? scale.ToString("0.##", CultureInfo.InvariantCulture) + "x"
                    : Math.Round(scale * 100).ToString(CultureInfo.InvariantCulture) + "pct");
                var path = Path.Combine(folder, safe + suffix + extension);
                using var encoded = SKImage.FromBitmap(output);
                using var data = encoded.Encode(skFormat, Math.Clamp(quality, 1, 100));
                File.WriteAllBytes(path, data.ToArray());
                written.Add(path);
            }
            return written;
        }
    }
}
