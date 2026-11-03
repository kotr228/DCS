using System;
using System.Collections.Generic;
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
    /// <summary>File > Export > Layers to Files: every visible layer as its own image.</summary>
    public partial class MainViewModel
    {
        [RelayCommand]
        private void ExportLayersToFiles()
        {
            if (IsSaving) return;

            var options = new FieldsDialogViewModel("Export Layers to Files",
                "Each visible layer (with its mask and layer style) is saved as its own file, named after the layer.");
            var format = options.Add(new ChoiceDialogField("Format", new[] { "PNG", "JPEG", "WebP" }));
            var trim = options.Add(new ChoiceDialogField("Size", new[] { "Trim to the layer's content", "Whole canvas" }));
            var prefix = options.Add(new TextDialogField("File name prefix", Path.GetFileNameWithoutExtension(ActiveDocument?.Title ?? "layer") + "_"));
            if (!FieldsDialog.Show(options)) return;

            var folderDialog = new OpenFolderDialog { Title = "Choose where to save the layers" };
            if (folderDialog.ShowDialog(Application.Current.MainWindow) != true) return;

            var saved = ExportLayers(folderDialog.FolderName, format.Selected, trim.Selected.StartsWith("Trim", StringComparison.Ordinal), prefix.Text);
            MessageBox.Show($"Saved {saved.Count} layer(s) to {folderDialog.FolderName}.", "Export Layers to Files",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Writes every visible, non-group, non-adjustment layer to
        /// <paramref name="folder"/>; returns the files written.</summary>
        internal List<string> ExportLayers(string folder, string format, bool trimToContent, string prefix)
        {
            Directory.CreateDirectory(folder);
            var scene = Layers.Scene;
            var written = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var (extension, skFormat) = format switch
            {
                "JPEG" => (".jpg", SKEncodedImageFormat.Jpeg),
                "WebP" => (".webp", SKEncodedImageFormat.Webp),
                _ => (".png", SKEncodedImageFormat.Png),
            };

            foreach (var layer in scene.Layers.Reverse()) // top first, like the Layers panel
            {
                if (layer.IsGroup || layer.IsAdjustment || !scene.IsEffectivelyVisible(layer)) continue;

                using var rendered = SceneCompositor.RenderStyledLayer(layer, applyMask: true);
                var area = trimToContent ? SelectionOps.TrimBounds(rendered, null) : new SKRectI(0, 0, rendered.Width, rendered.Height);
                if (area.IsEmpty) continue;

                using var piece = new SKBitmap(area.Width, area.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(piece))
                {
                    // JPEG has no transparency: put it on white.
                    canvas.Clear(skFormat == SKEncodedImageFormat.Jpeg ? SKColors.White : SKColors.Transparent);
                    canvas.DrawBitmap(rendered, -area.Left, -area.Top);
                }

                var name = SafeFileName(prefix + layer.Name);
                var unique = name;
                for (var n = 2; !used.Add(unique); n++) unique = $"{name}_{n}";
                var path = Path.Combine(folder, unique + extension);

                using var image = SKImage.FromBitmap(piece);
                using var data = image.Encode(skFormat, 92);
                File.WriteAllBytes(path, data.ToArray());
                written.Add(path);
            }
            return written;
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(cleaned) ? "layer" : cleaned;
        }
    }
}
