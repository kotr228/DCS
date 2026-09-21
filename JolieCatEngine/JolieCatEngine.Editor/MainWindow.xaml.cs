using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Shell;
using JolieCatEngine.Scripting.CSharp;

namespace JolieCatEngine.Editor
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Entity> SceneEntities { get; } = new();

        public ObservableCollection<string> Assets { get; } = new();

        private Entity? _selectedEntity;

        public Entity? SelectedEntity
        {
            get => _selectedEntity;
            private set
            {
                if (_selectedEntity == value)
                {
                    return;
                }

                if (_selectedEntity is not null)
                {
                    _selectedEntity.Transform.PropertyChanged -= SelectedTransform_PropertyChanged;
                }

                _selectedEntity = value;

                InspectorTransformPanel.DataContext = _selectedEntity?.Transform;
                InspectorTransformPanel.IsEnabled = _selectedEntity is not null;

                if (_selectedEntity is not null)
                {
                    _selectedEntity.Transform.PropertyChanged += SelectedTransform_PropertyChanged;
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            NativeBridge.Engine_Initialize();

            foreach (var entityName in new[] { "Main Camera", "Directional Light", "Player" })
            {
                var entity = new Entity(entityName)
                {
                    NativeId = NativeBridge.Engine_CreateEntity(),
                };
                entity.Transform.NativeId = entity.NativeId;
                SceneEntities.Add(entity);
            }

            SceneHierarchyTreeView.ItemsSource = SceneEntities;

            PopulateAssetBrowser();
            AssetBrowserItemsControl.ItemsSource = Assets;
        }

        private void PopulateAssetBrowser()
        {
            var buffer = new StringBuilder(4096);
            var writtenLength = NativeBridge.Engine_GetAssets(buffer, buffer.Capacity);

            if (writtenLength <= 0)
            {
                return;
            }

            foreach (var assetName in buffer.ToString().Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                Assets.Add(assetName);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var version = NativeBridge.Engine_GetVersion();
            MessageBox.Show($"JolieCatEngine.Core native bridge is linked.\nEngine_GetVersion() returned: {version}",
                "Native Bridge Test", MessageBoxButton.OK, MessageBoxImage.Information);

            NativeBridge.Engine_InitializeViewport(ViewportHost.Handle, (int)ViewportHost.ActualWidth, (int)ViewportHost.ActualHeight);
            NativeBridge.Engine_StartRenderLoop();
        }

        private void SceneHierarchyTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            SelectedEntity = e.NewValue as Entity;
        }

        private void SelectedTransform_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_selectedEntity is null || sender is not TransformComponent transform)
            {
                return;
            }

            (string Label, float Value)? field = e.PropertyName switch
            {
                nameof(TransformComponent.PositionX) => ("Position X", transform.PositionX),
                nameof(TransformComponent.PositionY) => ("Position Y", transform.PositionY),
                nameof(TransformComponent.PositionZ) => ("Position Z", transform.PositionZ),
                nameof(TransformComponent.RotationX) => ("Rotation X", transform.RotationX),
                nameof(TransformComponent.RotationY) => ("Rotation Y", transform.RotationY),
                nameof(TransformComponent.RotationZ) => ("Rotation Z", transform.RotationZ),
                nameof(TransformComponent.ScaleX) => ("Scale X", transform.ScaleX),
                nameof(TransformComponent.ScaleY) => ("Scale Y", transform.ScaleY),
                nameof(TransformComponent.ScaleZ) => ("Scale Z", transform.ScaleZ),
                _ => null,
            };

            if (field is null)
            {
                return;
            }

            DebugConsole.Log($"[Scene] '{_selectedEntity.Name}' {field.Value.Label} changed to {field.Value.Value:F3}");
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

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            NativeBridge.Engine_Shutdown();
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
