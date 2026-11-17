using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Filters;
using JolieCat.Shared.Enums;
using Microsoft.Win32;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>File &gt; Automate &gt; Photomerge: stitch several overlapping photos
    /// into a panorama, opened as a new document.</summary>
    public partial class MainViewModel
    {
        [RelayCommand]
        private void Photomerge()
        {
            if (IsSaving) return;
            var dialog = new OpenFileDialog
            {
                Title = "Photomerge: choose the overlapping photos (in order, left to right or top to bottom)",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*",
                Multiselect = true,
            };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
            if (dialog.FileNames.Length < 2)
            {
                MessageBox.Show("Choose at least two photos that overlap.", "Photomerge", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var shots = new List<SKBitmap>();
            try
            {
                foreach (var path in dialog.FileNames.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    shots.Add(SKBitmap.Decode(path) ?? throw new InvalidDataException($"Can't read {Path.GetFileName(path)}."));
                OpenPanorama(shots, Path.GetFileNameWithoutExtension(dialog.FileNames[0]) + " panorama");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Photomerge", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                foreach (var shot in shots) shot.Dispose();
            }
        }

        /// <summary>Stitches <paramref name="shots"/> and opens the result as a new document.</summary>
        internal DocumentViewModel OpenPanorama(IReadOnlyList<SKBitmap> shots, string name)
        {
            var (panorama, _) = MultiImage.Stitch(shots);
            using (panorama)
            {
                var document = new DocumentViewModel(Toolbox, name, ProjectType.StandardImage, panorama.Width, panorama.Height, null);
                RegisterDocument(document);
                var layer = document.Layers.Scene.Layers[0];
                layer.Canvas.Clear(SKColors.Transparent);
                layer.Canvas.DrawBitmap(panorama, 0, 0);
                document.Layers.RequestRepaint();
                Documents.Add(document);
                ActiveDocument = document;
                Canvas.ResetView();
                return document;
            }
        }
    }
}
