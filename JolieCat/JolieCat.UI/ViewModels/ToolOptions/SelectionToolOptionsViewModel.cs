using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>
    /// Options shown for every selection tool: the marquees, the lassos, Quick
    /// Selection, and Magic Wand. <see cref="Tolerance"/> drives Magic Wand and Quick
    /// Selection's flood-select color matching (see <c>CanvasViewModel.SelectByColor</c>);
    /// <see cref="FeatherRadius"/> and <see cref="AntiAlias"/> are recorded per tool but
    /// not yet consumed by the selection pipeline - a smooth-edged selection is a
    /// reasonable follow-up once this hard-edged one is in daily use.
    /// </summary>
    public partial class SelectionToolOptionsViewModel : ObservableObject
    {
        [ObservableProperty]
        private double featherRadius;

        [ObservableProperty]
        private double tolerance = 32;

        [ObservableProperty]
        private bool antiAlias = true;
    }
}
