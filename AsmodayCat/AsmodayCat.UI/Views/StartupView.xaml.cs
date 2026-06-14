using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class StartupView : Window
{
    private readonly StartupViewModel _vm;

    public StartupView(StartupViewModel vm)
    {
        _vm = vm;
        DataContext = _vm;
        InitializeComponent();
        Loaded += async (_, _) => await _vm.RunChecksAsync();

        // Update status icons when checks complete
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StartupViewModel.ServiceStatus))
                ServiceIcon.Text = _vm.ServiceStatus.Contains("Running") ? "✅" : "⚠";
            if (e.PropertyName == nameof(StartupViewModel.LlmStatus))
                LlmIcon.Text = _vm.LlmStatus == "Ready" ? "✅" : "⚠";
        };
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => DragMove();

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = App.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        Close();
    }
}
