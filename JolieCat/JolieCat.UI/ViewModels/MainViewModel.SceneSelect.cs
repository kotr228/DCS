using System;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Actions;
using JolieCat.Core.Documents;
using JolieCat.Core.Filters;
using JolieCat.Core.Rendering;
using JolieCat.UI.Views;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>Select &gt; Sky, Select &gt; Focus Area and Edit &gt; Sky Replacement.</summary>
    public partial class MainViewModel
    {
        /// <summary>Selects from a 0..255 mask of the document (half or more = selected).
        /// Returns false if nothing was found.</summary>
        internal bool SelectFromMask(byte[] mask)
        {
            int w = Layers.DocumentWidth, h = Layers.DocumentHeight;
            var path = SelectionOps.FromMask(w, h, i => mask[i] >= 128);
            var selection = Layers.Scene.Selection;
            if (path is null)
            {
                selection.Clear();
                Canvas.RequestRepaint();
                return false;
            }
            selection.SetPath(path, w, h);
            Canvas.RequestRepaint();
            return true;
        }

        /// <summary>Select &gt; Subject: the main subject of the photo.</summary>
        [RelayCommand]
        private void SelectSubject()
        {
            if (IsSaving || ActiveDocument is null) return;
            using var flat = SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);
            if (Core.Documents.SubjectSelect.Find(flat) is not { } mask || !SelectFromMask(mask))
                JolieCat.UI.MessageBox.Show("No subject stands out: it works best on a subject that differs in color from its background. Try Quick Selection or Color Range.",
                    "Select Subject", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        [RelayCommand]
        private void SelectSky()
        {
            if (IsSaving || ActiveDocument is null) return;
            using var flat = SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);
            if (!SelectFromMask(SceneDetect.SkyMask(flat)))
                JolieCat.UI.MessageBox.Show("No sky found: it should touch the top edge of the photo and be fairly plain.",
                    "Select Sky", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        [RelayCommand]
        private void SelectFocusArea()
        {
            if (IsSaving || ActiveDocument is null) return;
            var options = new FieldsDialogViewModel("Focus Area",
                "Selects the sharp, in-focus parts of the photo (the subject against a blurred background).");
            var sensitivity = options.Add(new SliderDialogField("Sensitivity", 0, 100, 50));
            if (!FieldsDialog.Show(options)) return;

            using var flat = SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);
            if (!SelectFromMask(SceneDetect.FocusMask(flat, (float)sensitivity.Value)))
                JolieCat.UI.MessageBox.Show("Nothing in focus was found - try a higher sensitivity.", "Focus Area",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        [RelayCommand]
        private void SkyReplacement()
        {
            if (!CanAdjustPixels()) return;
            var options = new FieldsDialogViewModel("Sky Replacement",
                "Finds the sky and puts a new one in. Shift Edge grows (positive) or shrinks the sky area; Foreground Lighting tints the rest to match.");
            var sky = options.Add(new ChoiceDialogField("Sky", SceneDetect.SkyPresets));
            var shift = options.Add(new SliderDialogField("Shift Edge", -50, 50, 0, "px"));
            var fade = options.Add(new SliderDialogField("Fade Edge", 0, 50, 4, "px"));
            var lighting = options.Add(new SliderDialogField("Foreground Lighting", 0, 100, 40, "%"));

            ActionStep Step() => new ActionStep { Kind = "Tone", Command = "SkyReplacement", Title = $"Sky Replacement ({sky.Selected})" }
                .With("Sky", sky.Selected).With("Shift", shift.Value).With("Fade", fade.Value).With("Lighting", lighting.Value);

            RunToneDialog(options, Step);
        }

        internal static SKBitmap ApplySkyReplacement(SKBitmap source, ActionStep step)
        {
            var name = step.Text("Sky");
            using var sky = SceneDetect.RenderSky(string.IsNullOrEmpty(name) ? SceneDetect.SkyPresets[0] : name,
                Math.Max(1, source.Width / 2), Math.Max(1, source.Height / 2));
            return SceneDetect.ReplaceSky(source, sky, (float)step.Number("Shift"), (float)step.Number("Fade", 4), (float)step.Number("Lighting", 40));
        }
    }
}
