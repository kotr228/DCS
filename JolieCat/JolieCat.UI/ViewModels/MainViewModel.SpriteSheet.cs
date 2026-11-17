using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class MainViewModel
    {
        /// <summary>File &gt; Pack Sprites into Sheet: several pictures (or the frames of a
        /// GIF) packed into a new Sprite Sheet document, its grid already set.</summary>
        [RelayCommand]
        private void PackSprites()
        {
            if (IsSaving) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pack Sprites: pick the pictures (in name order) or an animated GIF",
                Filter = "Images and animations|*.gif;*.webp;*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
                Multiselect = true,
            };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;

            List<(SKBitmap Picture, int Frames)>? pictures = null;
            try
            {
                pictures = Media.FrameDecoder.Decode(dialog.FileNames.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(), 24);
                if (pictures.Count == 0) return;
                var name = $"{Path.GetFileNameWithoutExtension(dialog.FileNames[0])} sheet";
                OpenPackedSheet(pictures.Select(p => p.Picture).ToList(), name);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't pack the sprites:\n{ex.Message}", "Pack Sprites", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (pictures is not null) foreach (var (picture, _) in pictures) picture.Dispose();
            }
        }

        /// <summary>File &gt; Import Sprite Atlas: a TexturePacker / Aseprite / JolieCat atlas
        /// (.json next to its .png) opened as a Sprite Sheet - one row per animation.</summary>
        [RelayCommand]
        private void ImportSpriteAtlas()
        {
            if (IsSaving) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Sprite Atlas: pick its .json (the picture is found next to it)",
                Filter = "Sprite atlas (*.json)|*.json|All files|*.*",
            };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
            try
            {
                var json = File.ReadAllText(dialog.FileName);
                var atlasInfo = SpriteAtlasReader.Read(json, null);
                var folder = Path.GetDirectoryName(dialog.FileName)!;
                var imagePath = atlasInfo.ImageName is { Length: > 0 } named ? Path.Combine(folder, named) : Path.ChangeExtension(dialog.FileName, ".png");
                if (!File.Exists(imagePath))
                {
                    var pick = new Microsoft.Win32.OpenFileDialog { Title = "The atlas picture wasn't found: pick it", Filter = "Images|*.png;*.webp;*.jpg|All files|*.*" };
                    if (pick.ShowDialog(Application.Current.MainWindow) != true) return;
                    imagePath = pick.FileName;
                }
                using var image = SKBitmap.Decode(imagePath) ?? throw new InvalidDataException("The atlas picture couldn't be read.");
                var atlas = SpriteAtlasReader.Read(json, image);
                if (atlas.Sprites.Count == 0) throw new InvalidDataException("No frames found in the .json.");
                try
                {
                    var (sheet, grid) = SpriteSheetTools.PackRows(SpriteAtlasReader.Rows(atlas));
                    using (sheet) OpenSheet(sheet, grid, Path.GetFileNameWithoutExtension(dialog.FileName));
                }
                finally
                {
                    foreach (var s in atlas.Sprites) s.Picture.Dispose();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                MessageBox.Show($"Couldn't import the atlas:\n{ex.Message}", "Import Sprite Atlas", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Opens <paramref name="pictures"/> packed as a new Sprite Sheet tab.</summary>
        internal DocumentViewModel OpenPackedSheet(IReadOnlyList<SKBitmap> pictures, string name)
        {
            var (sheet, grid) = SpriteSheetTools.Pack(pictures);
            using (sheet) return OpenSheet(sheet, grid, name);
        }

        /// <summary>Opens <paramref name="sheet"/> (with its grid) as a new Sprite Sheet tab.</summary>
        internal DocumentViewModel OpenSheet(SKBitmap sheet, SpriteSheetGrid grid, string name)
        {
            {
                var scene = new Scene(name);
                var layer = scene.AddLayer("Sprites", sheet.Width, sheet.Height);
                layer.Canvas.DrawBitmap(sheet, 0, 0);
                var document = new DocumentViewModel(Toolbox, name, ProjectType.SpriteSheet);
                RegisterDocument(document);
                document.Layers.LoadScene(scene, name, ProjectType.SpriteSheet, grid);
                Documents.Add(document);
                ActiveDocument = document;
                Canvas.ResetView();
                return document;
            }
        }

        /// <summary>Sprite Sheet panel &gt; Export for Game Engines: the sheet as a PNG
        /// plus a .json frame list next to it (TexturePacker layout).</summary>
        [RelayCommand]
        private void ExportSpriteSheetForEngines() => ExportSheetForEngines(packed: false);

        /// <summary>Sprite Sheet panel &gt; Export Packed Atlas: the sprites trimmed and
        /// packed tight (a smaller PNG) plus the .json that puts each back in its cell.</summary>
        [RelayCommand]
        private void ExportPackedAtlas() => ExportSheetForEngines(packed: true);

        private void ExportSheetForEngines(bool packed)
        {
            if (IsSaving || Layers.ProjectType != ProjectType.SpriteSheet) return;

            // Options first: the data layout, and for an atlas its spacing.
            var formats = new[] { "JSON (hash)", "JSON (array)", "CSS" };
            var options = new FieldsDialogViewModel(packed ? "Export Packed Atlas" : "Export for Game Engines",
                "JSON (hash): Phaser, PixiJS, Godot and Unity importers. JSON (array): Phaser, Construct. CSS: web pages.");
            var formatField = options.Add(new ChoiceDialogField("Data Format", formats));
            SliderDialogField? paddingField = null, extrudeField = null;
            ChoiceDialogField? powerField = null;
            if (packed)
            {
                paddingField = options.Add(new SliderDialogField("Padding", 0, 16, 1, "px"));
                extrudeField = options.Add(new SliderDialogField("Extrude (edge pixels repeated)", 0, 8, 1, "px"));
                powerField = options.Add(new ChoiceDialogField("Power-of-2 Size", new[] { "No", "Yes" }));
            }
            if (!Views.FieldsDialog.Show(options)) return;
            var format = (SpriteSheetTools.DataFormat)Array.IndexOf(formats, formatField.Selected);
            var dataExtension = format == SpriteSheetTools.DataFormat.Css ? ".css" : ".json";

            var title = ActiveDocument?.Title ?? "sheet";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = (packed ? "Export Packed Atlas" : "Export Sprite Sheet for Game Engines") + $" (PNG + {dataExtension.TrimStart('.').ToUpperInvariant()})",
                Filter = "PNG Image (*.png)|*.png",
                DefaultExt = ".png",
                FileName = $"{Path.GetFileNameWithoutExtension(title)}{(packed ? " atlas" : "")}.png",
            };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
            try
            {
                using var flat = Core.Rendering.SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);
                var name = Path.GetFileNameWithoutExtension(dialog.FileName);
                var imageName = Path.GetFileName(dialog.FileName);
                string json;
                SKBitmap? atlas = null;
                if (packed)
                {
                    var (picture, frames) = SpriteSheetTools.PackAtlas(flat, Layers.SpriteSheetGrid,
                        (int)Math.Round(paddingField!.Value), (int)Math.Round(extrudeField!.Value), powerField!.Selected == "Yes");
                    atlas = picture;
                    json = SpriteSheetTools.ToAtlasJson(picture, frames, Layers.SpriteSheetGrid, imageName, name, format);
                }
                else
                {
                    json = SpriteSheetTools.ToEngineJson(flat, Layers.SpriteSheetGrid, imageName, name, format);
                }
                using (atlas)
                using (var image = SKImage.FromBitmap(atlas ?? flat))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var file = File.Create(dialog.FileName))
                    data.SaveTo(file);
                File.WriteAllText(Path.ChangeExtension(dialog.FileName, dataExtension), json);
                SaveStatusMessage = $"✓ Exported {name}.png + {name}{dataExtension}";
                _ = ClearSaveStatusAfterDelayAsync(SaveStatusMessage);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't export the sheet:\n{ex.Message}", "Export Sprite Sheet", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Sprite Sheet panel &gt; Cells...: swap, copy, clear or mirror cells of the
        /// active layer, or give a cell its own pivot. Cells are numbered from 1, row by row.</summary>
        [RelayCommand]
        private void SpriteSheetCells()
        {
            if (IsSaving || Layers.ProjectType != ProjectType.SpriteSheet) return;
            var grid = Layers.SpriteSheetGrid;
            var count = Math.Max(1, grid.Columns) * Math.Max(1, grid.Rows);
            var actions = new[] { "Swap Two Cells", "Copy Cell To", "Clear Cell", "Flip Cell Horizontally", "Flip Cell Vertically", "Set Cell Pivot", "Reset Cell Pivot" };
            var options = new FieldsDialogViewModel("Cells",
                $"Cells are numbered 1 to {count}, left to right, top to bottom. Changes the active layer (one undo step); a pivot is that cell's own anchor in the engine export.");
            var action = options.Add(new ChoiceDialogField("Action", actions));
            var first = options.Add(new SliderDialogField("Cell", 1, count, 1));
            var second = options.Add(new SliderDialogField("Other Cell (swap / copy to)", 1, count, Math.Min(2, count)));
            var pivotX = options.Add(new SliderDialogField("Pivot X (set pivot)", 0, 100, grid.PivotX, "%"));
            var pivotY = options.Add(new SliderDialogField("Pivot Y (set pivot)", 0, 100, grid.PivotY, "%"));
            if (!Views.FieldsDialog.Show(options)) return;

            var a = (int)Math.Round(first.Value) - 1;
            var b = (int)Math.Round(second.Value) - 1;
            switch (action.Selected)
            {
                case "Set Cell Pivot":
                    Layers.SetCellPivot(a, pivotX.Value, pivotY.Value);
                    return;
                case "Reset Cell Pivot":
                    Layers.SetCellPivot(a, null, null);
                    return;
            }
            var kind = action.Selected switch
            {
                "Swap Two Cells" => SpriteSheetTools.CellAction.Swap,
                "Copy Cell To" => SpriteSheetTools.CellAction.Copy,
                "Clear Cell" => SpriteSheetTools.CellAction.Clear,
                "Flip Cell Horizontally" => SpriteSheetTools.CellAction.FlipHorizontal,
                _ => SpriteSheetTools.CellAction.FlipVertical,
            };
            if (!Layers.ChangeCells(kind, a, b))
                MessageBox.Show("Pick a pixel layer, and two different cells for Swap or Copy.", "Cells", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Sprite Sheet panel &gt; Detect Grid.</summary>
        [RelayCommand]
        private void DetectSpriteSheetGrid()
        {
            if (Layers.ProjectType != ProjectType.SpriteSheet) return;
            if (!Layers.DetectSpriteSheetGrid())
                MessageBox.Show("No grid shows: the sprites need empty (see-through or plain background) space between them.",
                    "Detect Grid", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
