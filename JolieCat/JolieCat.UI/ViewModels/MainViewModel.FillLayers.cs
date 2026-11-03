using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Core.Effects;
using JolieCat.Core.History;
using JolieCat.UI.Views;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>Layer > New Fill Layer (Solid Color / Gradient / Pattern) and editing a
    /// fill layer's content (Layer > Layer Content Options, or double-click its thumbnail).</summary>
    public partial class MainViewModel
    {
        /// <summary>Makes a fill layer above the active one (masked to the selection, if
        /// any), then opens its settings; Cancel takes it away again.</summary>
        [RelayCommand]
        private void NewFillLayer(string? kind)
        {
            if (IsSaving) return;
            var fillKind = Enum.TryParse<FillKind>(kind, out var k) ? k : FillKind.SolidColor;
            var fg = Canvas.BrushColor;
            var bg = Canvas.SecondaryBrushColor;
            var fill = new FillContent(fillKind, fg.WithAlpha(255), bg.WithAlpha(255));

            Layers.AddFillLayer(fill);
            var steps = Layers.History.UndoCount;
            if (Layers.ActiveLayer?.Model is not { IsFill: true } layer) return;
            if (!EditFill(layer, isNew: true) && Layers.History.UndoCount == steps)
                Layers.History.Undo(); // cancelled: no fill layer after all
        }

        /// <summary>Layer > Layer Content Options: edits the active fill layer.</summary>
        [RelayCommand]
        private void EditLayerContent()
        {
            if (Layers.ActiveLayer?.Model is { IsFill: true } layer) EditFill(layer, isNew: false);
        }

        /// <summary>The fill's settings, previewed live on the layer. True if OK.</summary>
        internal bool EditFill(Core.Documents.Layer layer, bool isNew)
        {
            var before = layer.Fill!;
            var options = new FieldsDialogViewModel(before.Title,
                isNew ? "The fill covers the canvas; with a selection it was limited to it by the layer's mask. Change it any time: double-click its thumbnail."
                      : "Changes show on the canvas at once.");

            var color = options.Add(new ColorDialogField(before.Kind == FillKind.Pattern ? "Pattern color" : before.Kind == FillKind.Gradient ? "Start color" : "Color", LayerStyleViewModel.Hex(before.Color)));
            ColorDialogField? color2 = null;
            SliderDialogField? angle = null;
            ChoiceDialogField? style = null;
            ChoiceDialogField? pattern = null;
            SliderDialogField? scale = null;
            if (before.Kind == FillKind.Gradient)
            {
                color2 = options.Add(new ColorDialogField("End color", LayerStyleViewModel.Hex(before.Color2)));
                style = options.Add(new ChoiceDialogField("Style", new[] { "Linear", "Radial" }, before.Radial ? "Radial" : "Linear"));
                angle = options.Add(new SliderDialogField("Angle", -180, 180, before.Angle, "°"));
            }
            else if (before.Kind == FillKind.Pattern)
            {
                Media.PatternLibrary.EnsureLoaded();
                color2 = options.Add(new ColorDialogField("Background", LayerStyleViewModel.Hex(before.Color2)));
                pattern = options.Add(new ChoiceDialogField("Pattern", PatternTiles.Names.ToList(), before.PatternName));
                scale = options.Add(new SliderDialogField("Scale", 10, 1000, before.Scale, "%"));
            }

            FillContent Current() => before with
            {
                Color = LayerStyleViewModel.Parse(color.Hex, before.Color),
                Color2 = color2 is null ? before.Color2 : LayerStyleViewModel.Parse(color2.Hex, before.Color2),
                Angle = (float)(angle?.Value ?? before.Angle),
                Radial = style?.Selected == "Radial",
                PatternName = pattern?.Selected ?? before.PatternName,
                Scale = (float)(scale?.Value ?? before.Scale),
            };

            void Preview()
            {
                layer.Fill = Current();
                layer.RenderFill();
                Layers.NotifyContentChanged();
            }
            options.Changed += Preview;

            var applied = FieldsDialog.Show(options);
            options.Changed -= Preview;
            var after = Current();
            if (!applied || after == before)
            {
                layer.Fill = before;
                layer.RenderFill();
                Layers.NotifyContentChanged();
                return applied;
            }

            layer.Fill = after;
            layer.RenderFill();
            Layers.History.Push(new FillChangeCommand(Layers.Scene, layer, before, after), $"Edit {after.Title}");
            Layers.NotifyContentChanged();
            return true;
        }
    }
}
