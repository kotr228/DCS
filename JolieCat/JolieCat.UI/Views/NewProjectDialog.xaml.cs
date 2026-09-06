using System.Windows;
using JolieCat.Shared.Enums;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// Prompts for one of the three project types (see <see cref="ProjectType"/>) when
    /// creating a new document - <c>MainViewModel.NewDocument</c>'s own dialog. Reports
    /// back via <see cref="SelectedProjectType"/> once <see cref="Window.DialogResult"/>
    /// is true, the same "read a settled result off a plain property" convention every
    /// other modal dialog in this app uses (most of them through a bound view model
    /// instead, since this one's "result" is just a single choice with nothing else to
    /// preview or configure).
    /// </summary>
    public partial class NewProjectDialog : Window
    {
        public ProjectType SelectedProjectType { get; private set; } = ProjectType.StandardImage;

        public NewProjectDialog()
        {
            InitializeComponent();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            SelectedProjectType = SpriteSheetOption.IsChecked == true ? ProjectType.SpriteSheet
                : ClipbarAnimationOption.IsChecked == true ? ProjectType.ClipbarAnimation
                : ProjectType.StandardImage;

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
