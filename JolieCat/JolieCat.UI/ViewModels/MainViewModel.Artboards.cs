using System;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.UI.Views;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class MainViewModel
    {
        /// <summary>Common sizes for a new artboard.</summary>
        private static readonly (string Name, int W, int H)[] ArtboardPresets =
        {
            ("Custom", 0, 0),
            ("App Icon 1024", 1024, 1024),
            ("Icon 512", 512, 512),
            ("Icon 256", 256, 256),
            ("Phone 1080 × 1920", 1080, 1920),
            ("Tablet 2048 × 1536", 2048, 1536),
            ("Web 1920 × 1080", 1920, 1080),
            ("Instagram Post 1080", 1080, 1080),
            ("Instagram Story 1080 × 1920", 1080, 1920),
            ("YouTube Thumbnail 1280 × 720", 1280, 720),
            ("Banner 1500 × 500", 1500, 500),
            ("Favicon 32", 32, 32),
        };

        /// <summary>Image &gt; Artboards: "FromSelection", "New", "Delete" or "Clear".</summary>
        [RelayCommand]
        private void Artboard(string what)
        {
            if (IsSaving || ActiveDocument is null) return;
            switch (what)
            {
                case "FromSelection":
                {
                    var selection = Layers.Scene.Selection;
                    if (!selection.HasSelection || selection.Region is not { } region)
                    {
                        MessageBox.Show("Select the area first (for example with the Rectangular Marquee).", "New Artboard from Selection",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var options = new FieldsDialogViewModel("New Artboard from Selection", $"{region.Bounds.Width} × {region.Bounds.Height} px");
                    var name = options.Add(new TextDialogField("Name", Layers.NextArtboardName()));
                    if (!FieldsDialog.Show(options)) return;
                    Layers.AddArtboard(name.Text, region.Bounds);
                    break;
                }

                case "New":
                {
                    var options = new FieldsDialogViewModel("New Artboard",
                        "A named area exported as its own picture (File > Export > Artboards to Files). A preset sets the size; X and Y place its top-left corner.");
                    var name = options.Add(new TextDialogField("Name", Layers.NextArtboardName()));
                    var preset = options.Add(new ChoiceDialogField("Size", ArtboardPresets.Select(p => p.Name).ToList(), ArtboardPresets[2].Name));
                    var w = options.Add(new SliderDialogField("Width (Custom)", 1, Math.Max(1, Layers.DocumentWidth), Math.Min(512, Layers.DocumentWidth), "px"));
                    var h = options.Add(new SliderDialogField("Height (Custom)", 1, Math.Max(1, Layers.DocumentHeight), Math.Min(512, Layers.DocumentHeight), "px"));
                    var x = options.Add(new SliderDialogField("X", 0, Math.Max(0, Layers.DocumentWidth - 1), 0, "px"));
                    var y = options.Add(new SliderDialogField("Y", 0, Math.Max(0, Layers.DocumentHeight - 1), 0, "px"));
                    if (!FieldsDialog.Show(options)) return;
                    var chosen = ArtboardPresets.First(p => p.Name == preset.Selected);
                    var width = chosen.W > 0 ? chosen.W : (int)Math.Round(w.Value);
                    var height = chosen.H > 0 ? chosen.H : (int)Math.Round(h.Value);
                    var bounds = SKRectI.Create((int)Math.Round(x.Value), (int)Math.Round(y.Value), width, height);
                    if (!Layers.AddArtboard(name.Text, bounds))
                        MessageBox.Show("That artboard would be outside the canvas.", "New Artboard", MessageBoxButton.OK, MessageBoxImage.Information);
                    else if (bounds.Right > Layers.DocumentWidth || bounds.Bottom > Layers.DocumentHeight)
                        MessageBox.Show("The artboard was bigger than the canvas there, so it was cut to the canvas. Make the canvas bigger (Image > Canvas Size) for the full size.",
                            "New Artboard", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                }

                case "Delete":
                {
                    if (Layers.Artboards.Count == 0) return;
                    var options = new FieldsDialogViewModel("Delete Artboard");
                    var name = options.Add(new ChoiceDialogField("Artboard", Layers.Artboards.Select(a => a.Name).ToList()));
                    if (!FieldsDialog.Show(options)) return;
                    Layers.RemoveArtboard(name.Selected);
                    break;
                }

                case "Clear":
                    Layers.ClearArtboards();
                    break;
            }
        }

        /// <summary>File &gt; Export &gt; Artboards to Files: one PNG per artboard, named after it.</summary>
        [RelayCommand]
        private void ExportArtboards()
        {
            if (IsSaving || ActiveDocument is null) return;
            if (Layers.Artboards.Count == 0)
            {
                MessageBox.Show("This document has no artboards yet (Image > Artboards).", "Export Artboards",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder for the artboards" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var written = WriteArtboards(dialog.FolderName);
                SaveStatusMessage = $"✓ Exported {written} artboard(s)";
                _ = ClearSaveStatusAfterDelayAsync(SaveStatusMessage);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"Couldn't export the artboards:\n{ex.Message}", "Export Artboards", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Writes each artboard as "&lt;name&gt;.png" into <paramref name="folder"/>.</summary>
        internal int WriteArtboards(string folder)
        {
            var pictures = Layers.RenderArtboards();
            try
            {
                foreach (var (name, picture) in pictures)
                {
                    var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                    using var image = SKImage.FromBitmap(picture);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var file = File.Create(Path.Combine(folder, safe + ".png"));
                    data.SaveTo(file);
                }
                return pictures.Count;
            }
            finally
            {
                foreach (var (_, picture) in pictures) picture.Dispose();
            }
        }
    }
}
