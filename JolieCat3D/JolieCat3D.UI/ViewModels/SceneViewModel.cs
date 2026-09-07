using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.UI.ViewModels
{
    /// <summary>
    /// The Scene Outliner's own data source: a <see cref="NodeViewModel"/> tree mirroring
    /// a <see cref="Scene3D"/>'s <see cref="Scene3D.RootNodes"/>/<see cref="Node.Children"/>
    /// hierarchy, plus which one is currently selected (bound by the Properties Inspector).
    /// Built once via <see cref="Load"/> against a scene <c>MainWindow</c> already
    /// constructed - this class doesn't own or create the scene itself, only wraps it for
    /// display/editing.
    /// </summary>
    public partial class SceneViewModel : ObservableObject
    {
        private readonly Dictionary<Node, NodeViewModel> _lookup = new();

        public ObservableCollection<NodeViewModel> RootNodes { get; } = new();

        [ObservableProperty]
        private NodeViewModel? selectedNode;

        /// <summary>Raised whenever any <see cref="NodeViewModel"/> in this tree edits
        /// its wrapped <see cref="Node"/> (a Properties Inspector field changed) -
        /// <c>MainWindow</c>'s cue to re-render the viewport and reposition the gizmo,
        /// the same way <c>TransformGizmo.TransformChanged</c> is for a gizmo drag.</summary>
        public event EventHandler? SceneChanged;

        public void Load(Scene3D scene)
        {
            ArgumentNullException.ThrowIfNull(scene);

            RootNodes.Clear();
            _lookup.Clear();

            foreach (var root in scene.RootNodes)
                RootNodes.Add(BuildViewModel(root));
        }

        private NodeViewModel BuildViewModel(Node node)
        {
            var viewModel = new NodeViewModel(node, RaiseSceneChanged);
            _lookup[node] = viewModel;

            foreach (var child in node.Children)
                viewModel.Children.Add(BuildViewModel(child));

            return viewModel;
        }

        private void RaiseSceneChanged() => SceneChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>The <see cref="NodeViewModel"/> wrapping <paramref name="node"/> -
        /// what lets a viewport click (which only ever resolves to a Core <see cref="Node"/>,
        /// via <c>Scene3DRenderer.HitTest</c>) update <see cref="SelectedNode"/> to the
        /// matching view model the Outliner/Properties panel actually bind against.</summary>
        public NodeViewModel? FindViewModel(Node? node) =>
            node is not null && _lookup.TryGetValue(node, out var viewModel) ? viewModel : null;

        partial void OnSelectedNodeChanged(NodeViewModel? oldValue, NodeViewModel? newValue)
        {
            if (oldValue is not null) oldValue.IsSelected = false;
            if (newValue is not null) newValue.IsSelected = true;
        }
    }
}
