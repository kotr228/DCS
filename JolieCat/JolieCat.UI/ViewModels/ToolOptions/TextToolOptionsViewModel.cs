using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Options shown for the Horizontal Text and Vertical Text tools.</summary>
    public partial class TextToolOptionsViewModel : ObservableObject
    {
        public IReadOnlyList<string> AvailableFontFamilies { get; } = new[]
        {
            "Segoe UI", "Arial", "Georgia", "Consolas", "Times New Roman", "Verdana"
        };

        [ObservableProperty]
        private string fontFamily = "Segoe UI";

        [ObservableProperty]
        private double fontSize = 24;

        [ObservableProperty]
        private bool isBold;

        [ObservableProperty]
        private bool isItalic;
    }
}
