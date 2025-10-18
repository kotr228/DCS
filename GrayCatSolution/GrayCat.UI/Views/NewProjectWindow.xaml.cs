namespace GrayCat.UI.Views;

using GrayCat.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

public partial class NewProjectWindow : Window
{
    public NewProjectViewModel ViewModel => (NewProjectViewModel)DataContext;

    public NewProjectWindow()
    {
        InitializeComponent();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Select output directory",
            FileName = "SelectFolder",
            Filter = "Folder|*.none",
            CheckFileExists = false,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.OutputPath = System.IO.Path.GetDirectoryName(dialog.FileName);
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.ProjectName))
        {
            System.Windows.MessageBox.Show(
                "Please enter a project name",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(ViewModel.OutputPath))
        {
            System.Windows.MessageBox.Show(
                "Please select an output directory",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ProgressBar.Visibility = Visibility.Visible;
        DialogResult = true;
    }
}