using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using JolieCat.UI.ViewModels.Timeline;

namespace JolieCat.UI.Views.Timeline
{
    /// <summary>
    /// Visual block for one <see cref="TimelineClipViewModel"/>: a colored bar that can
    /// be dragged to move (its body) or dragged at either edge to trim/resize. Position
    /// and size on screen are driven entirely by bindings (see <c>TimelinePanelView</c>);
    /// this code-behind only translates Thumb drag deltas into view-model calls.
    /// </summary>
    public partial class TimelineClipControl : UserControl
    {
        public TimelineClipControl()
        {
            InitializeComponent();
        }

        // Named ClipViewModel (not "Clip") to avoid hiding UIElement.Clip, the geometry
        // property WPF uses for clipping this control's own visual.
        private TimelineClipViewModel? ClipViewModel => DataContext as TimelineClipViewModel;

        private void BodyThumb_DragDelta(object sender, DragDeltaEventArgs e) => ClipViewModel?.DragBy(e.HorizontalChange);

        private void LeftEdgeThumb_DragDelta(object sender, DragDeltaEventArgs e) => ClipViewModel?.ResizeStartBy(e.HorizontalChange);

        private void RightEdgeThumb_DragDelta(object sender, DragDeltaEventArgs e) => ClipViewModel?.ResizeEndBy(e.HorizontalChange);
    }
}
