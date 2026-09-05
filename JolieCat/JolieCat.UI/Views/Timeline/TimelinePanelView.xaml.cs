using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using JolieCat.UI.ViewModels.Timeline;

namespace JolieCat.UI.Views.Timeline
{
    /// <summary>
    /// The Bottom zone's whole animation clipbar: toolbar, frame ruler with a scrubbable
    /// playhead, and the track rows (each with its clips and keyframe markers). Expects
    /// its DataContext to be a <see cref="TimelineViewModel"/> (MainWindow sets this via
    /// <c>DataContext="{Binding Timeline}"</c>).
    /// </summary>
    public partial class TimelinePanelView : UserControl
    {
        public TimelinePanelView()
        {
            InitializeComponent();
        }

        private void PlayheadThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (DataContext is TimelineViewModel timeline)
                timeline.ScrubPlayheadBy(e.HorizontalChange);
        }
    }
}
