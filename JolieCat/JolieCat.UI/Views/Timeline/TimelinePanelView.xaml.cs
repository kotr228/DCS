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

        private static KeyframeViewModel? KeyOf(object sender) => (sender as System.Windows.FrameworkElement)?.DataContext as KeyframeViewModel;

        private void KeyThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (DataContext is TimelineViewModel timeline && KeyOf(sender) is { } key)
                timeline.BeginKeyDrag(key, (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0);
        }

        private void KeyThumb_DragDelta(object sender, DragDeltaEventArgs e) => (DataContext as TimelineViewModel)?.DragKeyBy(e.HorizontalChange);

        private void KeyThumb_DragCompleted(object sender, DragCompletedEventArgs e) => (DataContext as TimelineViewModel)?.EndKeyDrag();

        private void PlayheadThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (DataContext is TimelineViewModel timeline)
                timeline.ScrubPlayheadBy(e.HorizontalChange);
        }
    }
}
