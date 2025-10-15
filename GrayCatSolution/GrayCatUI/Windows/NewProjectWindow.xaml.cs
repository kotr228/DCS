using GrayCat.Shared.Models;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls; // Явно вказуємо простір імен

namespace GrayCat.UI.Windows
{
    public partial class NewProjectWindow : Window
    {
        public ProjectGenerationRequest? GenerationRequest { get; private set; }

        public NewProjectWindow()
        {
            InitializeComponent();
            ProjectTypeComboBox.ItemsSource = System.Enum.GetValues(typeof(ProjectType)).Cast<ProjectType>();
            ProjectTypeComboBox.SelectedIndex = 0;
            LocationTextBox.Text = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "GrayCatProjects");

            ProjectNameTextBox.TextChanged += ProjectNameTextBox_TextChanged;
        }

        private void ProjectNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Явно вказуємо, що це System.Windows.Controls.TextBox
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;

            string invalidChars = new string(Path.GetInvalidFileNameChars());
            string sanitizedText = new string(textBox.Text.Where(ch => !invalidChars.Contains(ch)).ToArray());

            if (textBox.Text != sanitizedText)
            {
                var selectionStart = textBox.SelectionStart;
                textBox.Text = sanitizedText;
                textBox.CaretIndex = selectionStart > sanitizedText.Length ? sanitizedText.Length : selectionStart;
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.InitialDirectory = LocationTextBox.Text;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                LocationTextBox.Text = dialog.SelectedPath;
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ProjectNameTextBox.Text) || string.IsNullOrWhiteSpace(LocationTextBox.Text))
            {
                System.Windows.MessageBox.Show("Project Name and Location cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GenerationRequest = new ProjectGenerationRequest
            {
                ProjectName = ProjectNameTextBox.Text.Trim(),
                OutputPath = LocationTextBox.Text.Trim(),
                Author = AuthorTextBox.Text.Trim(),
                Type = (ProjectType)ProjectTypeComboBox.SelectedItem
            };

            ProgressBar.Visibility = Visibility.Visible;
            DialogResult = true;
        }
    }
}