using System.Windows.Controls;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// The Sprite Sheet slicing grid's editing panel - columns/rows/padding/margin,
    /// "snap Marquee to grid" toggle, and the "Slice &amp; Export Frames" action - shown
    /// in MainWindow.xaml's right column only for a <c>ProjectType.SpriteSheet</c>
    /// project (see <c>ViewModels.Layers.LayersViewModel.IsSpriteSheetProject</c>).
    /// </summary>
    public partial class SpriteSheetGridPanelView : UserControl
    {
        public SpriteSheetGridPanelView()
        {
            InitializeComponent();
        }
    }
}
