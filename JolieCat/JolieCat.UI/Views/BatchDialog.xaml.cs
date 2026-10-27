using System.ComponentModel;
using System.Windows;
using JolieCat.UI.ViewModels;
using Microsoft.Win32;

namespace JolieCat.UI.Views
{
    /// <summary>File &gt; Automate &gt; Batch (see <see cref="BatchViewModel"/>).</summary>
    public partial class BatchDialog : Window
    {
        public BatchDialog()
        {
            InitializeComponent();
        }

        private BatchViewModel ViewModel => (BatchViewModel)DataContext;

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            if (PickFolder("Choose the folder with the images to process", ViewModel.SourceFolder) is { } folder)
                ViewModel.SourceFolder = folder;
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            if (PickFolder("Choose where to save the results", ViewModel.OutputFolder) is { } folder)
                ViewModel.OutputFolder = folder;
        }

        private string? PickFolder(string title, string current)
        {
            var dialog = new OpenFolderDialog { Title = title };
            if (System.IO.Directory.Exists(current)) dialog.InitialDirectory = current;
            return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            // Closing mid-run stops after the current file.
            if (ViewModel.IsRunning) ViewModel.CancelRunCommand.Execute(null);
            base.OnClosing(e);
        }
    }
}
