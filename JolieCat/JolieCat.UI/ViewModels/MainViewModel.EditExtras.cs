using System;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Clipboard;
using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;
using JolieCat.UI.Views;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>Edit > Cut / Copy Merged / Paste in Place / Stroke / Fade, and Select >
    /// Grow / Similar / Load Layer Transparency / Save and Load Selection.</summary>
    public partial class MainViewModel
    {
        private DocumentPanelsViewModel? _panels;

        /// <summary>The Navigator / Info / Histogram panels.</summary>
        public DocumentPanelsViewModel Panels => _panels ??= new DocumentPanelsViewModel(() => ActiveDocument);

        /// <summary>Where the last copied pixels came from (Paste in Place puts them back there).</summary>
        private SKPointI? _clipboardOrigin;

        /// <summary>Ctrl+X: copies the selected pixels, then clears them.</summary>
        [RelayCommand]
        private void Cut()
        {
            if (Layers.ActiveLayer?.Model is not { IsGroup: false } layer || layer.HasSmartFilters) return;
            CopySelection();
            Canvas.DeleteSelectedPixels();
        }

        /// <summary>Shift+Ctrl+C: copies what you see (all visible layers) in the selection.</summary>
        [RelayCommand]
        private void CopyMerged()
        {
            int width = Layers.DocumentWidth, height = Layers.DocumentHeight;
            using var flat = Core.Rendering.SceneCompositor.Flatten(Layers.Scene, width, height);
            var selection = Layers.Scene.Selection;
            var bounds = selection.HasSelection && selection.Region is { } region
                ? SKRectI.Intersect(region.Bounds, new SKRectI(0, 0, width, height))
                : new SKRectI(0, 0, width, height);
            if (bounds.IsEmpty) return;

            var copy = new SKBitmap(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(copy))
            {
                canvas.Clear(SKColors.Transparent);
                if (selection.HasSelection && selection.Path is { } path)
                {
                    using var moved = new SKPath(path);
                    moved.Transform(SKMatrix.CreateTranslation(-bounds.Left, -bounds.Top));
                    canvas.ClipPath(moved, antialias: true);
                }
                canvas.DrawBitmap(flat, -bounds.Left, -bounds.Top);
            }
            PixelClipboard.SetContent(copy);
            _clipboardOrigin = new SKPointI(bounds.Left, bounds.Top);
            PasteCommand.NotifyCanExecuteChanged();
            PasteInPlaceCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Shift+Ctrl+V: pastes as a new layer exactly where the pixels were copied from.</summary>
        [RelayCommand(CanExecute = nameof(CanPaste))]
        private void PasteInPlace()
        {
            if (PixelClipboard.Content is not { } clip) return;
            var at = _clipboardOrigin ?? new SKPointI((Layers.DocumentWidth - clip.Width) / 2, (Layers.DocumentHeight - clip.Height) / 2);
            Layers.PasteAsNewLayer(clip, at.X, at.Y);
        }

        private string? _strokeColor;
        private double _strokeWidth = 4;
        private string _strokeLocation = nameof(StrokeLocation.Center);

        /// <summary>Edit > Stroke: outlines the selection.</summary>
        [RelayCommand]
        private void StrokeSelection()
        {
            if (!Layers.Scene.Selection.HasSelection)
            {
                MessageBox.Show("Select an area first - the stroke follows the selection's edge.", "Stroke", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var options = new FieldsDialogViewModel("Stroke", "Draws a line along the selection's edge on the active layer.");
            var width = options.Add(new SliderDialogField("Width", 1, 250, _strokeWidth, "px"));
            var fg = Canvas.BrushColor;
            var color = options.Add(new ColorDialogField("Color", _strokeColor ?? $"#{fg.Red:X2}{fg.Green:X2}{fg.Blue:X2}"));
            var location = options.Add(new ChoiceDialogField("Location", Enum.GetNames<StrokeLocation>(), _strokeLocation));
            var opacity = options.Add(new SliderDialogField("Opacity", 1, 100, 100, "%"));
            if (!FieldsDialog.Show(options)) return;

            _strokeWidth = width.Value;
            _strokeColor = color.Hex;
            _strokeLocation = location.Selected;
            Canvas.StrokeSelection((float)width.Value, LayerStyleViewModel.Parse(color.Hex, SKColors.Black),
                Enum.Parse<StrokeLocation>(location.Selected), opacity.Value);
        }

        /// <summary>Edit > Fade (Shift+Ctrl+F): right after a filter, adjustment, fill or
        /// brush stroke, mixes it back with how the layer was - at any opacity and blend mode.</summary>
        [RelayCommand]
        private void Fade()
        {
            var name = Layers.History.UndoNames.Count > 0 ? Layers.History.UndoNames[^1] : "Edit";
            if (!Canvas.BeginFade(name))
            {
                MessageBox.Show("Fade works right after a filter, adjustment, fill or brush stroke on a layer.", "Fade",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var options = new FieldsDialogViewModel($"Fade {name}", "Lowers the strength of the last change, or blends it with a different mode.");
            var opacity = options.Add(new SliderDialogField("Opacity", 0, 100, 100, "%"));
            var mode = options.Add(new ChoiceDialogField("Mode", Enum.GetNames<BlendMode>(), nameof(BlendMode.Normal)));
            void Preview() => Canvas.UpdateFade(opacity.Value, Enum.Parse<BlendMode>(mode.Selected));
            options.Changed += Preview;
            Preview();

            if (FieldsDialog.Show(options)) Canvas.CommitAdjustment();
            else Canvas.CancelAdjustment();
        }

        private double _growTolerance = 32;

        /// <summary>Select > Grow (touching pixels) / Similar (anywhere).</summary>
        [RelayCommand]
        private void GrowSelection(string? kind)
        {
            if (!Layers.Scene.Selection.HasSelection) return;
            var similar = kind == "Similar";
            var dialog = new NumberPromptDialog(similar ? "Similar" : "Grow", "Tolerance", _growTolerance, 0, 255, "") { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;
            _growTolerance = dialog.Value;
            Canvas.GrowSelection((int)Math.Round(dialog.Value), contiguous: !similar);
        }

        /// <summary>Select > Save Selection: keeps the selection under a name (saved with the project).</summary>
        [RelayCommand]
        private void SaveSelection()
        {
            if (!Layers.Scene.Selection.HasSelection)
            {
                MessageBox.Show("There's no selection to save.", "Save Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var n = 1;
            while (Layers.Scene.SavedSelections.ContainsKey($"Selection {n}")) n++;
            var options = new FieldsDialogViewModel("Save Selection", "Saved selections are kept in the project; bring one back with Select > Load Selection.");
            var name = options.Add(new TextDialogField("Name", $"Selection {n}"));
            options.Validate = () => string.IsNullOrWhiteSpace(name.Text) ? "Type a name." : null;
            if (!FieldsDialog.Show(options)) return;

            if (Layers.Scene.SavedSelections.ContainsKey(name.Text.Trim())
                && MessageBox.Show($"Replace the saved selection \"{name.Text.Trim()}\"?", "Save Selection", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            Canvas.SaveSelection(name.Text);
        }

        /// <summary>Select > Load Selection: a saved selection, as new or combined with the current one.</summary>
        [RelayCommand]
        private void LoadSelection()
        {
            var names = Canvas.SavedSelectionNames;
            if (names.Count == 0)
            {
                MessageBox.Show("No saved selections in this document yet (Select > Save Selection).", "Load Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var options = new FieldsDialogViewModel("Load Selection");
            var name = options.Add(new ChoiceDialogField("Selection", names));
            var mode = options.Add(new ChoiceDialogField("Operation",
                new[] { "New Selection", "Add to Selection", "Subtract from Selection", "Intersect with Selection" }));
            if (!FieldsDialog.Show(options)) return;

            var combine = mode.Selected switch
            {
                "Add to Selection" => SelectionCombine.Add,
                "Subtract from Selection" => SelectionCombine.Subtract,
                "Intersect with Selection" => SelectionCombine.Intersect,
                _ => SelectionCombine.New,
            };
            Canvas.LoadSelection(name.Selected, combine);
        }

        [RelayCommand]
        private void LoadLayerTransparency() => Canvas.SelectLayerTransparency();

        /// <summary>Image > Content-Aware Scale: resizes the image by taking away (or
        /// repeating) the least noticeable seams, so people and objects keep their shape.
        /// The selection, if any, can be protected.</summary>
        [RelayCommand]
        private void ContentAwareScale()
        {
            if (IsSaving) return;
            int width = Layers.DocumentWidth, height = Layers.DocumentHeight;
            var options = new FieldsDialogViewModel("Content-Aware Scale",
                "Plain areas (sky, walls, water) are taken out or repeated first. Protect a subject by selecting it and choosing Protect: Selection.");
            var w = options.Add(new SliderDialogField("Width", 30, 200, 100, "%"));
            var h = options.Add(new SliderDialogField("Height", 30, 200, 100, "%"));
            var protect = options.Add(new ChoiceDialogField("Protect", new[] { "None", "Selection" },
                Layers.Scene.Selection.HasSelection ? "Selection" : "None"));
            if (!FieldsDialog.Show(options)) return;

            var newWidth = Math.Max(1, (int)Math.Round(width * w.Value / 100));
            var newHeight = Math.Max(1, (int)Math.Round(height * h.Value / 100));
            if (newWidth == width && newHeight == height) return;

            bool[]? keep = null;
            var selection = Layers.Scene.Selection;
            if (protect.Selected == "Selection" && selection.HasSelection)
            {
                keep = new bool[width * height];
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                        keep[y * width + x] = selection.Contains(x, y);
            }

            var previous = System.Windows.Input.Mouse.OverrideCursor;
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                using var flat = Core.Rendering.SceneCompositor.Flatten(Layers.Scene, width, height);
                var plan = Core.Filters.SeamCarvingPlan.Create(flat, newWidth, newHeight, keep);
                Layers.ContentAwareScaleDocument(plan);
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = previous;
            }
        }

        /// <summary>Select > Select and Mask (Alt+Ctrl+R): smooth, shift and feather the selection's edge.</summary>
        [RelayCommand]
        private void SelectAndMask()
        {
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection)
            {
                MessageBox.Show("Make a selection first.", "Select and Mask", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var options = new FieldsDialogViewModel("Select and Mask",
                "Smooth rounds off jagged edges and drops specks; Shift Edge moves the edge out (+) or in (-); Feather softens it. The outline previews live.");
            var smooth = options.Add(new SliderDialogField("Smooth", 0, 30, 0, "px"));
            var shift = options.Add(new SliderDialogField("Shift Edge", -50, 50, 0, "px"));
            var feather = options.Add(new SliderDialogField("Feather", 0, 100, selection.Feather, "px"));
            void Preview() => Canvas.PreviewRefineSelection((float)smooth.Value, (float)shift.Value);
            options.Changed += Preview;

            if (FieldsDialog.Show(options)) Canvas.RefineSelection((float)smooth.Value, (float)shift.Value, (float)feather.Value);
            else Canvas.CancelRefineSelection();
        }

        /// <summary>Image > Duplicate: the whole document - layers, masks, styles, Smart
        /// Filters, guides, saved selections - as a new tab.</summary>
        [RelayCommand]
        private void DuplicateDocument()
        {
            if (IsSaving || ActiveDocument is not { } source) return;
            var name = $"{source.Title} copy";

            var scene = new Scene(name);
            scene.RestoreLayers(source.Layers.Scene.CaptureLayers(), source.Layers.Scene.IndexOf(source.Layers.Scene.ActiveLayer!));
            scene.RestoreGuides(source.Layers.Scene.Guides);
            foreach (var (key, value) in source.Layers.Scene.SavedSelections) scene.SavedSelections[key] = value;

            var document = new DocumentViewModel(Toolbox, name, source.ProjectType);
            RegisterDocument(document);
            document.Layers.LoadScene(scene, name, source.ProjectType);
            Documents.Add(document);
            ActiveDocument = document;
            Canvas.ResetView();
        }
    }
}
