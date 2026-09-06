using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Tools;
using JolieCat.Shared.Enums;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// One collapsible section of the Tools panel - all the tools in a single
    /// <see cref="ToolCategory"/>, plus whether that section is currently expanded.
    /// Replaces a plain <c>IGrouping&lt;ToolCategory, ToolDefinition&gt;</c> (what LINQ's
    /// GroupBy alone would produce) because a grouping has nowhere to hold that expanded/
    /// collapsed UI state - this does, as an ordinary bindable property, and computes the
    /// header text once instead of the view re-deriving it every time it binds.
    /// </summary>
    public sealed partial class ToolCategoryGroupViewModel : ObservableObject
    {
        public ToolCategory Category { get; }

        public string DisplayName { get; }

        public IReadOnlyList<ToolDefinition> Tools { get; }

        [ObservableProperty]
        private bool isExpanded = true;

        public ToolCategoryGroupViewModel(ToolCategory category, IReadOnlyList<ToolDefinition> tools)
        {
            Category = category;
            Tools = tools;
            DisplayName = category switch
            {
                ToolCategory.Selection => "Selection",
                ToolCategory.Navigation => "Navigation",
                ToolCategory.Painting => "Painting",
                ToolCategory.Retouching => "Retouching",
                ToolCategory.VectorText => "Vector / Text",
                ToolCategory.Transform => "Transform",
                _ => category.ToString(),
            };
        }
    }
}
