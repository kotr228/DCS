namespace GrayCat.UI.Views;

using GrayCat.UI.Localization;
using GrayCat.UI.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;

public partial class NewProjectWindow : Controls.ThemedWindow
{
    public NewProjectViewModel ViewModel => (NewProjectViewModel)DataContext;

    public NewProjectWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("Choose where to create the project"),
            InitialDirectory = Directory.Exists(ViewModel.OutputPath) ? ViewModel.OutputPath : null
        };

        if (dialog.ShowDialog(this) == true)
            ViewModel.OutputPath = dialog.FolderName;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsValid)
        {
            ThemedMessageBox.Show(this, ViewModel.ValidationError, Loc.T("Create Project"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
