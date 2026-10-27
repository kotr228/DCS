using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Tools;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels.ToolOptions;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Owns the tool palette: which tools exist, which one is active, and the
    /// tool-specific option set the Properties panel should currently show. Composed
    /// into <see cref="MainViewModel"/> rather than flattened into it, so the toolbox
    /// stays self-contained as more panels (layers, history, timeline) join it.
    /// </summary>
    public partial class ToolboxViewModel : ObservableObject
    {
        /// <summary>Every tool, in catalog order.</summary>
        public IReadOnlyList<ToolDefinition> Tools { get; } = ToolCatalog.All;

        /// <summary>Tools grouped by <see cref="ToolCategory"/> for the Tools panel's
        /// collapsible sections, in catalog order.</summary>
        public IReadOnlyList<ToolCategoryGroupViewModel> ToolsByCategory { get; } =
            ToolCatalog.All
                .GroupBy(tool => tool.Category)
                .Select(group => new ToolCategoryGroupViewModel(group.Key, group.ToList()))
                .ToList();

        [ObservableProperty]
        private ToolDefinition activeTool;

        /// <summary>The option view model the Properties panel should display for <see cref="ActiveTool"/>.</summary>
        [ObservableProperty]
        private object currentToolOptions;

        /// <summary>One options instance per tool, kept for the session, so a tool's
        /// settings (brush size, tolerance, font...) survive switching away and back.</summary>
        private readonly Dictionary<ToolType, object> _optionsByTool = new();

        public ToolboxViewModel()
        {
            activeTool = Tools[0];
            currentToolOptions = GetOptions(activeTool.Type);
        }

        [RelayCommand]
        private void SelectTool(ToolDefinition? tool)
        {
            if (tool is not null)
                ActiveTool = tool;
        }

        public void SelectTool(ToolType type)
        {
            var tool = Tools.FirstOrDefault(t => t.Type == type);
            if (tool is not null) ActiveTool = tool;
        }

        /// <summary>Last tool used from each shortcut group, so "M" brings back whichever
        /// marquee you used last (Photoshop behavior).</summary>
        private readonly Dictionary<string, ToolDefinition> _lastToolByShortcut = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>Keyboard tool switching, Photoshop-style: a letter selects the
        /// group's most recently used tool (staying put if one is already active);
        /// with <paramref name="cycle"/> (Shift+letter) it steps to the next tool in the
        /// group instead. Returns false if no tool uses that letter.</summary>
        public bool SelectToolByShortcut(string letter, bool cycle = false)
        {
            var matches = Tools.Where(t => string.Equals(t.Shortcut, letter, System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) return false;

            var currentIndex = matches.IndexOf(ActiveTool);
            if (cycle)
                ActiveTool = matches[(currentIndex + 1) % matches.Count];
            else if (currentIndex < 0)
                ActiveTool = _lastToolByShortcut.TryGetValue(letter, out var last) ? last : matches[0];

            return true;
        }

        partial void OnActiveToolChanged(ToolDefinition value)
        {
            CurrentToolOptions = GetOptions(value.Type);
            if (!string.IsNullOrEmpty(value.Shortcut))
                _lastToolByShortcut[value.Shortcut] = value;
        }

        private object GetOptions(ToolType type)
        {
            if (!_optionsByTool.TryGetValue(type, out var options))
            {
                options = CreateOptions(type);
                _optionsByTool[type] = options;
            }

            return options;
        }

        private static object CreateOptions(ToolType type) => type switch
        {
            // Brush/Pencil paint with the foreground color; Eraser and the retouching
            // tools don't, so their shared Properties view hides the color picker for them.
            ToolType.Brush or ToolType.Pencil => new PaintToolOptionsViewModel(showColorPicker: true),

            ToolType.Eraser or ToolType.CloneStamp or ToolType.HealingBrush or ToolType.SpotHealing or ToolType.Blur or
                ToolType.Sharpen or ToolType.Sponge or ToolType.Dodge or ToolType.Burn =>
                new PaintToolOptionsViewModel(showColorPicker: false),

            ToolType.TextHorizontal or ToolType.TextVertical => new TextToolOptionsViewModel(),

            ToolType.RectangularMarquee or ToolType.EllipticalMarquee or ToolType.Lasso or
                ToolType.PolygonalLasso or ToolType.MagneticLasso or ToolType.QuickSelection or
                ToolType.MagicWand => new SelectionToolOptionsViewModel(),

            // Path Selection/Direct Selection edit the same working path the Pen tool
            // draws, so all three share its options (the stroke width a "Stroke Path"
            // action paints with, and the Convert to Selection/Stroke/Fill buttons).
            ToolType.Pen or ToolType.PathSelection or ToolType.DirectSelection => new PenToolOptionsViewModel(),

            ToolType.Shape => new ShapeToolOptionsViewModel(),

            ToolType.PaintBucket => new FillToolOptionsViewModel(),

            ToolType.Gradient => new GradientToolOptionsViewModel(),

            ToolType.Eyedropper => new EyedropperToolOptionsViewModel(),

            ToolType.Crop => new CropToolOptionsViewModel(),

            ToolType.FreeTransform => new FreeTransformToolOptionsViewModel(),

            ToolType.Warp => new WarpToolOptionsViewModel(),

            _ => new EmptyToolOptionsViewModel(),
        };
    }
}
