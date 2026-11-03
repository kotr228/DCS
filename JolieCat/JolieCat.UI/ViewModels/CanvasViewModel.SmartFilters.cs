using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Actions;
using JolieCat.Core.Effects;
using JolieCat.Core.History;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Smart Filters on the active layer: filters applied to a converted layer are kept
    /// as a list over its untouched original, so they can be edited, hidden, reordered
    /// or removed later (Layer > Smart Filters). Each change is one undo step.
    /// </summary>
    public partial class CanvasViewModel
    {
        static CanvasViewModel()
        {
            // How a Smart Filter's step is run: the same filters Actions play back.
            SmartFilterEngine.Apply ??= (step, bitmap) => MainViewModel.FilterFor(step)?.Invoke(bitmap);
        }

        /// <summary>A filter dialog's OK on a Smart Filter layer: drops the pixel preview
        /// and adds <paramref name="step"/> to the layer's filters instead. False (nothing
        /// done) for an ordinary layer.</summary>
        public bool CommitAsSmartFilter(ActionStep step)
        {
            if (_adjustmentLayer is not { HasSmartFilters: true } layer) return false;
            CancelAdjustment();
            AddSmartFilter(layer, step);
            return true;
        }

        public void AddSmartFilter(Core.Documents.Layer layer, ActionStep step) =>
            ChangeSmartFilters(layer, layer.SmartFilters.Append(new SmartFilter(step)).ToList(), $"Smart Filter: {Title(step)}");

        /// <summary>Replaces the layer's filter list (one undo step named <paramref name="historyName"/>).</summary>
        public void ChangeSmartFilters(Core.Documents.Layer layer, IReadOnlyList<SmartFilter> filters, string historyName)
        {
            if (!layer.HasSmartFilters) return;
            var before = layer.SmartFilters;
            layer.ReplaceSmartFilters(filters);
            Layers.History.Push(new SmartFilterChangeCommand(Layers.Scene, layer, before, filters), historyName);
            Layers.NotifyContentChanged();
            RaiseInvalidate();
        }

        /// <summary>True (with a hint) when <paramref name="layer"/>'s own pixels can't be
        /// edited because it shows Smart Filters' result (its mask can be).</summary>
        internal bool BlockedBySmartFilters(Core.Documents.Layer layer)
        {
            if (!layer.HasSmartFilters || layer.PaintsMask) return false;
            ToolHintText = "This layer has Smart Filters - use Layer > Smart Filters > Rasterize to change its pixels";
            RaiseInvalidate();
            return true;
        }

        /// <summary>Moves a Smart Filter layer's original by <paramref name="delta"/> and runs
        /// its filters again; returns the undo entry.</summary>
        internal IEditCommand ShiftSmartFilterSource(Core.Documents.Layer layer, SkiaSharp.SKPointI delta)
        {
            var source = layer.SmartFilterSource!;
            var before = source.Pixels;
            using (var copy = source.Copy())
            using (var canvas = new SkiaSharp.SKCanvas(source))
            {
                canvas.Clear(SkiaSharp.SKColors.Transparent);
                canvas.DrawBitmap(copy, delta.X, delta.Y);
            }
            layer.RenderSmartFilters();
            return new SmartFilterSourceCommand(Layers.Scene, layer, before, source.Pixels);
        }

        private static string Title(ActionStep step) => string.IsNullOrEmpty(step.Title) ? step.Command : step.Title;
    }
}
