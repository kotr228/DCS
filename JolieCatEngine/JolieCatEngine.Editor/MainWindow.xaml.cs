using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using JolieCatEngine.Scripting.CSharp;

namespace JolieCatEngine.Editor
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Entity> SceneEntities { get; } = new();

        public ObservableCollection<string> Assets { get; } = new();

        private Entity? _selectedEntity;

        private bool _isPanningCamera;
        private Point _lastCameraPanPoint;
        private float _cameraX;
        private float _cameraY;
        private float _cameraZoom = 1f;

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

        private void ViewportInputOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton is not (MouseButton.Middle or MouseButton.Right))
            {
                return;
            }

            _isPanningCamera = true;
            _lastCameraPanPoint = e.GetPosition(ViewportInputOverlay);
            ViewportInputOverlay.CaptureMouse();
        }

        private void ViewportInputOverlay_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton is not (MouseButton.Middle or MouseButton.Right))
            {
                return;
            }

            _isPanningCamera = false;
            ViewportInputOverlay.ReleaseMouseCapture();
        }

        private void ViewportInputOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanningCamera)
            {
                return;
            }

            var currentPoint = e.GetPosition(ViewportInputOverlay);
            var delta = currentPoint - _lastCameraPanPoint;
            _lastCameraPanPoint = currentPoint;

            const float panSpeed = 0.02f;
            _cameraX -= (float)delta.X * panSpeed;
            _cameraY -= (float)delta.Y * panSpeed;

            NativeBridge.Engine_SetEditorCamera(_cameraX, _cameraY, _cameraZoom);
        }

        private void ViewportInputOverlay_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            const float zoomStep = 0.1f;
            const float minZoom = 0.1f;
            const float maxZoom = 5.0f;

            _cameraZoom = Math.Clamp(_cameraZoom + Math.Sign(e.Delta) * zoomStep, minZoom, maxZoom);

            NativeBridge.Engine_SetEditorCamera(_cameraX, _cameraY, _cameraZoom);
        }

        private void AssetCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: string assetName } element)
            {
                return;
            }

            DragDrop.DoDragDrop(element, assetName, DragDropEffects.Copy);
        }

        private void AssetDropTarget_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                return;
            }

            if (e.Data.GetData(DataFormats.StringFormat) is not string assetName)
            {
                return;
            }

            var entity = new Entity(Path.GetFileNameWithoutExtension(assetName))
            {
                NativeId = NativeBridge.Engine_CreateEntity(),
            };
            entity.Transform.NativeId = entity.NativeId;
            SceneEntities.Add(entity);

            DebugConsole.Log($"[Editor] Dropped asset '{assetName}' and spawned new Entity '{entity.Name}' (Id {entity.NativeId})");
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
