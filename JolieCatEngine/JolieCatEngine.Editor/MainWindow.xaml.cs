using System.Windows;
using System.Windows.Shell;
using JolieCatEngine.Scripting.CSharp;

namespace JolieCatEngine.Editor
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var version = NativeBridge.Engine_GetVersion();
            MessageBox.Show($"JolieCatEngine.Core native bridge is linked.\nEngine_GetVersion() returned: {version}",
                "Native Bridge Test", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            var isMaximized = WindowState == WindowState.Maximized;
            MaximizeRestoreGlyph.Text = isMaximized ? "" : "";
            MaximizeRestoreButton.ToolTip = isMaximized ? "Restore" : "Maximize";

            MaxHeight = isMaximized ? SystemParameters.WorkArea.Height : double.PositiveInfinity;
            MaxWidth = isMaximized ? SystemParameters.WorkArea.Width : double.PositiveInfinity;
        }
    }
}
