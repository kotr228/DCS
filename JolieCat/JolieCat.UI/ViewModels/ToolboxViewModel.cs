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

        /// <summary>Tools grouped by <see cref="ToolCategory"/> for the Tools panel's sections.</summary>
        public IReadOnlyList<IGrouping<ToolCategory, ToolDefinition>> ToolsByCategory { get; } =
            ToolCatalog.All.GroupBy(tool => tool.Category).ToList();

        [ObservableProperty]
        private ToolDefinition activeTool;

        /// <summary>The option view model the Properties panel should display for <see cref="ActiveTool"/>.</summary>
        [ObservableProperty]
        private object currentToolOptions;

        public ToolboxViewModel()
        {
            activeTool = Tools[0];
            currentToolOptions = CreateOptions(activeTool.Type);
        }

        [RelayCommand]
        private void SelectTool(ToolDefinition? tool)
        {
            if (tool is not null)
                ActiveTool = tool;
        }

        partial void OnActiveToolChanged(ToolDefinition value) => CurrentToolOptions = CreateOptions(value.Type);

        private static object CreateOptions(ToolType type) => type switch
        {
            ToolType.Brush or ToolType.Pencil or ToolType.Eraser or ToolType.CloneStamp or
                ToolType.HealingBrush or ToolType.Blur or ToolType.Sharpen or ToolType.Sponge or
                ToolType.Dodge or ToolType.Burn => new PaintToolOptionsViewModel(),

            ToolType.TextHorizontal or ToolType.TextVertical => new TextToolOptionsViewModel(),

            ToolType.RectangularMarquee or ToolType.EllipticalMarquee or ToolType.Lasso or
                ToolType.PolygonalLasso or ToolType.MagneticLasso or ToolType.QuickSelection or
                ToolType.MagicWand => new SelectionToolOptionsViewModel(),

            ToolType.Pen or ToolType.Shape => new ShapeToolOptionsViewModel(),

            ToolType.PaintBucket => new FillToolOptionsViewModel(),

            ToolType.Gradient => new GradientToolOptionsViewModel(),

            _ => new EmptyToolOptionsViewModel(),
        };
    }
}
