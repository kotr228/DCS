using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using JolieCat.Core.Documents;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.Layers;

namespace JolieCat.UI
{
    /// <summary>
    /// Layers panel behaviour that needs the ListBox itself: selecting several layers
    /// (Ctrl/Shift+click) and drag and drop. The ListBox's own selection is kept in step
    /// with <see cref="LayersViewModel.ActiveLayer"/> and
    /// <see cref="LayersViewModel.SelectedLayers"/> both ways.
    /// </summary>
    public partial class MainWindow
    {
        private const string LayerDragFormat = "JolieCat.Layer";

        private LayersViewModel? _attachedLayers;
        private bool _syncingLayerSelection;
        private Point _layerDragStart;
        private LayerViewModel? _layerDragCandidate;
        private LayerViewModel? _pendingSingleSelect;
        private LayerViewModel? _dropHintRow;

        /// <summary>Called once the window's view model exists.</summary>
        private void InitializeLayersPanel()
        {
            if (ViewModel is not { } vm) return;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainViewModel.Layers) or nameof(MainViewModel.ActiveDocument))
                    AttachLayers(vm.Layers);
            };
            AttachLayers(vm.Layers);
        }

        private void AttachLayers(LayersViewModel layers)
        {
            if (ReferenceEquals(layers, _attachedLayers)) return;

            if (_attachedLayers is not null)
            {
                _attachedLayers.PropertyChanged -= OnLayersPropertyChanged;
                _attachedLayers.Items.CollectionChanged -= OnLayerRowsChanged;
            }

            _attachedLayers = layers;
            layers.PropertyChanged += OnLayersPropertyChanged;
            layers.Items.CollectionChanged += OnLayerRowsChanged;

            // The ItemsSource binding updates after this; select once it has.
            Dispatcher.BeginInvoke(SyncLayerListFromModel, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnLayersPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LayersViewModel.ActiveLayer)) SyncLayerListFromModel();
        }

        private void OnLayerRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                Dispatcher.BeginInvoke(SyncLayerListFromModel, System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Selects the active layer in the list - plus the rest of the
        /// multi-selection, if the active layer is part of it.</summary>
        private void SyncLayerListFromModel()
        {
            if (_syncingLayerSelection || _attachedLayers is not { } layers) return;

            var active = layers.ActiveLayer;
            var wanted = active is not null && layers.SelectedLayers.Contains(active.Model)
                ? layers.SelectedLayers.ToHashSet()
                : active is null ? new System.Collections.Generic.HashSet<Layer>() : new System.Collections.Generic.HashSet<Layer> { active.Model };

            _syncingLayerSelection = true;
            try
            {
                LayersList.SelectedItems.Clear();
                foreach (var item in layers.Items)
                    if (wanted.Contains(item.Model)) LayersList.SelectedItems.Add(item);
                layers.SetSelectedLayers(wanted);
            }
            finally
            {
                _syncingLayerSelection = false;
            }
        }

        private void LayersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingLayerSelection || _attachedLayers is not { } layers) return;

            var selected = LayersList.SelectedItems.Cast<LayerViewModel>().ToList();
            if (selected.Count == 0)
            {
                // Rows being rebuilt, or a click that deselected everything: keep the active one.
                Dispatcher.BeginInvoke(SyncLayerListFromModel, System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            // The last row clicked becomes the active layer.
            var newActive = e.AddedItems.Count > 0 ? (LayerViewModel)e.AddedItems[e.AddedItems.Count - 1]!
                : layers.ActiveLayer is { } current && selected.Contains(current) ? current
                : selected[^1];

            _syncingLayerSelection = true;
            try
            {
                layers.SetSelectedLayers(selected.Select(item => item.Model));
                layers.ActiveLayer = newActive;
            }
            finally
            {
                _syncingLayerSelection = false;
            }
        }

        // ================= Drag and drop =================

        private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
        {
            while (element is not null and not T)
                element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            return element as T;
        }

        private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                if (FindDescendant<T>(child) is { } deeper) return deeper;
            }
            return null;
        }

        private void LayersList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _layerDragCandidate = null;
            _pendingSingleSelect = null;

            var source = e.OriginalSource as DependencyObject;
            if (FindAncestor<ButtonBase>(source) is not null || FindAncestor<TextBox>(source) is not null) return;
            if (FindAncestor<ListBoxItem>(source) is not { DataContext: LayerViewModel row } item) return;

            _layerDragStart = e.GetPosition(LayersList);
            _layerDragCandidate = row;

            // Pressing on a row of a multi-selection mustn't collapse the selection yet:
            // the press may start a drag of all of them. A plain click selects just it
            // on release instead.
            if (item.IsSelected && LayersList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && e.ClickCount == 1)
            {
                _pendingSingleSelect = row;
                e.Handled = true;
            }
        }

        private void LayersList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _layerDragCandidate is not { } dragged) return;

            var delta = e.GetPosition(LayersList) - _layerDragStart;
            if (System.Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                System.Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _pendingSingleSelect = null;
            _layerDragCandidate = null;
            DragDrop.DoDragDrop(LayersList, new DataObject(LayerDragFormat, dragged), DragDropEffects.Move);
            ClearDropHint();
        }

        private void LayersList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_pendingSingleSelect is { } row)
            {
                _pendingSingleSelect = null;
                LayersList.SelectedItems.Clear();
                LayersList.SelectedItems.Add(row);
            }
            _layerDragCandidate = null;
        }

        /// <summary>Where a drop at the pointer would put the dragged layer, or null if
        /// it can't go there.</summary>
        private (LayerViewModel Target, LayerDropPosition Position)? DropTarget(DragEventArgs e)
        {
            if (_attachedLayers is not { } layers || e.Data.GetData(LayerDragFormat) is not LayerViewModel dragged) return null;
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { DataContext: LayerViewModel target } item) return null;
            if (ReferenceEquals(target, dragged)) return null;

            var moving = layers.SelectedLayers.Contains(dragged.Model) ? layers.SelectedLayers : new[] { dragged.Model };
            foreach (var layer in moving)
                if (ReferenceEquals(layer, target.Model) || (layer.IsGroup && layers.Scene.IsDescendantOf(target.Model, layer))) return null;

            var fraction = e.GetPosition(item).Y / System.Math.Max(1, item.ActualHeight);
            LayerDropPosition position;
            if (target.IsGroup)
                position = fraction < 0.3 ? LayerDropPosition.Above
                    : fraction > 0.7 && !target.IsExpanded ? LayerDropPosition.Below
                    : LayerDropPosition.Into;
            else
                position = fraction < 0.5 ? LayerDropPosition.Above : LayerDropPosition.Below;

            return (target, position);
        }

        private void LayersList_DragOver(object sender, DragEventArgs e)
        {
            var drop = DropTarget(e);
            ClearDropHint();
            if (drop is { } d)
            {
                _dropHintRow = d.Target;
                d.Target.DropAbove = d.Position == LayerDropPosition.Above;
                d.Target.DropBelow = d.Position == LayerDropPosition.Below;
                d.Target.DropInto = d.Position == LayerDropPosition.Into;
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            // Scroll when dragging near the list's top or bottom edge.
            if (FindDescendant<ScrollViewer>(LayersList) is { } scroll)
            {
                var y = e.GetPosition(LayersList).Y;
                if (y < 20) scroll.LineUp();
                else if (y > LayersList.ActualHeight - 20) scroll.LineDown();
            }

            e.Handled = true;
        }

        private void LayersList_DragLeave(object sender, DragEventArgs e) => ClearDropHint();

        private void LayersList_Drop(object sender, DragEventArgs e)
        {
            var drop = DropTarget(e);
            ClearDropHint();
            if (drop is not { } d || _attachedLayers is not { } layers || e.Data.GetData(LayerDragFormat) is not LayerViewModel dragged) return;

            layers.MoveLayers(dragged.Model, d.Target.Model, d.Position);
            e.Handled = true;
        }

        private void ClearDropHint()
        {
            _dropHintRow?.ClearDropHint();
            _dropHintRow = null;
        }
    }
}
