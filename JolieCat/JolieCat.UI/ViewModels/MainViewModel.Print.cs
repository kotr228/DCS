using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Rendering;
using JolieCat.UI.Media;
using JolieCat.UI.Views;

namespace JolieCat.UI.ViewModels
{
    /// <summary>File &gt; Print: the flattened image, fitted to the page or at a print
    /// resolution, centered.</summary>
    public partial class MainViewModel
    {
        [RelayCommand]
        private void Print()
        {
            if (IsSaving || ActiveDocument is null) return;

            var options = new FieldsDialogViewModel("Print",
                "Fit to Page fills the printable area. Print Resolution prints at a size set by pixels per inch (300 is photo quality).");
            var mode = options.Add(new ChoiceDialogField("Size", new[] { "Fit to Page", "Print Resolution" }));
            var ppi = options.Add(new SliderDialogField("Resolution", 72, 600, 300, "ppi"));
            if (!FieldsDialog.Show(options)) return;

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            using var flat = SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);
            var image = SkiaImageConverter.ToThumbnail(flat, Math.Max(flat.Width, flat.Height));
            var page = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            var size = PrintSize(flat.Width, flat.Height, page, mode.Selected == "Fit to Page" ? null : ppi.Value);

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
                context.DrawImage(image, new Rect((page.Width - size.Width) / 2, (page.Height - size.Height) / 2, size.Width, size.Height));
            dialog.PrintVisual(visual, ActiveDocument.Title);
        }

        /// <summary>Printed size in WPF units (1/96 inch): fitted inside
        /// <paramref name="page"/> keeping the shape, or at <paramref name="ppi"/>
        /// pixels per inch (shrunk to fit if that is larger than the page).</summary>
        internal static Size PrintSize(int width, int height, Size page, double? ppi)
        {
            var fit = Math.Min(page.Width / width, page.Height / height);
            var scale = ppi is { } p && p > 0 ? Math.Min(96.0 / p, fit) : fit;
            return new Size(width * scale, height * scale);
        }
    }
}
