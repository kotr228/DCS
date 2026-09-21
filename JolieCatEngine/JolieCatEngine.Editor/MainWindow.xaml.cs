using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Shell;
using JolieCatEngine.Scripting.CSharp;

namespace JolieCatEngine.Editor
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Entity> SceneEntities { get; } = new()
        {
            new Entity("Main Camera"),
            new Entity("Directional Light"),
            new Entity("Player"),
        };

        public MainWindow()
        {
            InitializeComponent();
            SceneHierarchyTreeView.ItemsSource = SceneEntities;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var version = NativeBridge.Engine_GetVersion();
            MessageBox.Show($"JolieCatEngine.Core native bridge is linked.\nEngine_GetVersion() returned: {version}",
                "Native Bridge Test", MessageBoxButton.OK, MessageBoxImage.Information);

            NativeBridge.Engine_InitializeViewport(ViewportHost.Handle, (int)ViewportHost.ActualWidth, (int)ViewportHost.ActualHeight);
        }

        private void SceneHierarchyTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            InspectorTransformPanel.DataContext = (e.NewValue as Entity)?.Transform;
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
