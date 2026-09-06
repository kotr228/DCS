using System.Windows.Controls;

namespace JolieCat.UI.Views.Timeline
{
    /// <summary>
    /// The Timeline's dedicated, full-workspace view for a Clipbar Animation project:
    /// a playback transport bar (play/pause, step-by-frame, jump to start/end,
    /// total-frames/frame-rate settings) above the same ruler/track-lane visuals
    /// <see cref="TimelinePanelView"/> renders, given the whole center workspace area
    /// instead of a small bottom-docked strip. See <c>MainWindow.xaml</c>'s Design/
    /// Timeline workspace-mode switch for how a document's
    /// <see cref="ViewModels.WorkspaceMode"/> selects between this and the ordinary
    /// canvas view.
    /// </summary>
    public partial class TimelineWorkspaceView : UserControl
    {
        public TimelineWorkspaceView()
        {
            InitializeComponent();
        }
    }
}
