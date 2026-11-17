using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.UI.ViewModels
{
    /// <summary>One shape key row in the Inspector: name, value slider, validity.</summary>
    public sealed class ShapeKeyViewModel : ObservableObject
    {
        private readonly Node _node;
        private readonly Action _onChanged;

        public ShapeKeyViewModel(Node node, ShapeKey key, Action onChanged)
        {
            _node = node;
            Key = key;
            _onChanged = onChanged;
        }

        public ShapeKey Key { get; }

        public Node Node => _node;

        public string Name => Key.Name;

        /// <summary>False after a topology edit changed the vertex count (the key is then ignored).</summary>
        public bool IsValid => _node.Mesh is { } mesh && ShapeKeys.IsValid(mesh, Key);

        public double Value
        {
            get => Key.Value;
            set
            {
                if (Math.Abs(Key.Value - value) < 1e-6) return;
                Key.Value = (float)value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }
}
