using System.Collections.ObjectModel;
using System.Numerics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.UI.ViewModels
{
    /// <summary>
    /// Wraps one <see cref="Node"/> for the Scene Outliner (as a
    /// <c>HierarchicalDataTemplate</c> item, via <see cref="Children"/>) and the
    /// Properties Inspector (via its Position/Rotation/Scale/DiffuseColor properties) -
    /// the same "hand-written properties delegating to a wrapped Core model, not
    /// auto-generated backing fields" convention JolieCat.UI's own LayerViewModel uses
    /// for <c>JolieCat.Core.Documents.Layer</c>, since every property here reads/writes
    /// state that actually lives on <see cref="Node"/>, not on this view model itself.
    /// </summary>
    public partial class NodeViewModel : ObservableObject
    {
        private readonly Node _node;
        private readonly Action _onChanged;

        // Rotation is cached here in degrees, not re-derived from LocalRotation on every
        // property read - EulerAngles.ToDegrees is a many-to-one mapping (multiple angle
        // triples can represent the same rotation), so recomputing it fresh after every
        // single-axis edit could visibly snap the OTHER two fields to a different (if
        // equivalent) reading mid-edit. Refreshed from the quaternion only on
        // SyncFromCore (selection changes, or an external change like a gizmo drag).
        private double _rotationX;
        private double _rotationY;
        private double _rotationZ;

        /// <summary>The wrapped Core node - <c>MainWindow</c> needs this to hand off to
        /// <c>Scene3DRenderer.Select</c>/<c>TransformGizmo.Attach</c> when the Outliner's
        /// own selection changes, since those operate on <see cref="Node"/>, not this
        /// view model.</summary>
        public Node UnderlyingNode => _node;

        public ObservableCollection<NodeViewModel> Children { get; } = new();

        [ObservableProperty]
        private bool isSelected;

        public NodeViewModel(Node node, Action onChanged)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
            SyncFromCore();
        }

        public string Name
        {
            get => _node.Name;
            set
            {
                if (_node.Name == value) return;
                _node.Name = value;
                OnPropertyChanged();
                _onChanged();
            }
        }

        public double PositionX
        {
            get => _node.LocalPosition.X;
            set { var p = _node.LocalPosition; _node.LocalPosition = new Vector3((float)value, p.Y, p.Z); OnPropertyChanged(); _onChanged(); }
        }

        public double PositionY
        {
            get => _node.LocalPosition.Y;
            set { var p = _node.LocalPosition; _node.LocalPosition = new Vector3(p.X, (float)value, p.Z); OnPropertyChanged(); _onChanged(); }
        }

        public double PositionZ
        {
            get => _node.LocalPosition.Z;
            set { var p = _node.LocalPosition; _node.LocalPosition = new Vector3(p.X, p.Y, (float)value); OnPropertyChanged(); _onChanged(); }
        }

        /// <summary>Degrees - see this class's own remarks on why these three read from
        /// a cached field rather than decomposing <see cref="Node.LocalRotation"/> fresh
        /// on every access.</summary>
        public double RotationX
        {
            get => _rotationX;
            set { _rotationX = value; ApplyRotation(); OnPropertyChanged(); _onChanged(); }
        }

        public double RotationY
        {
            get => _rotationY;
            set { _rotationY = value; ApplyRotation(); OnPropertyChanged(); _onChanged(); }
        }

        public double RotationZ
        {
            get => _rotationZ;
            set { _rotationZ = value; ApplyRotation(); OnPropertyChanged(); _onChanged(); }
        }

        public double ScaleX
        {
            get => _node.LocalScale.X;
            set { var s = _node.LocalScale; _node.LocalScale = new Vector3((float)value, s.Y, s.Z); OnPropertyChanged(); _onChanged(); }
        }

        public double ScaleY
        {
            get => _node.LocalScale.Y;
            set { var s = _node.LocalScale; _node.LocalScale = new Vector3(s.X, (float)value, s.Z); OnPropertyChanged(); _onChanged(); }
        }

        public double ScaleZ
        {
            get => _node.LocalScale.Z;
            set { var s = _node.LocalScale; _node.LocalScale = new Vector3(s.X, s.Y, (float)value); OnPropertyChanged(); _onChanged(); }
        }

        /// <summary>True only once this node actually has a <see cref="Node.Mesh"/> with
        /// its own <see cref="Core.Materials.Material"/> - what the Properties panel binds
        /// its material-color swatch's Visibility to, since an empty pivot node (or a
        /// mesh authored with no material at all) has no color to show or edit.</summary>
        public bool HasMaterial => _node.Mesh?.Material is not null;

        public Color DiffuseColor
        {
            get
            {
                var color = _node.Mesh?.Material?.DiffuseColor ?? Color4.White;
                return Color.FromScRgb(1f, Clamp01(color.R), Clamp01(color.G), Clamp01(color.B));
            }
            set
            {
                if (_node.Mesh?.Material is not { } material) return;
                material.DiffuseColor = new Color4(value.ScR, value.ScG, value.ScB);
                OnPropertyChanged();
                _onChanged();
            }
        }

        /// <summary>The same color as <see cref="DiffuseColor"/>, as a "#RRGGBB" string -
        /// the compact hex-field editing convention JolieCat.UI's own ColorPickerPanel
        /// already uses, rather than three separate 0-255 R/G/B fields. An unparsable
        /// typed value is simply ignored (the property re-reads and re-displays the last
        /// good color instead) rather than throwing back into the text box mid-edit.</summary>
        public string DiffuseColorHex
        {
            get => $"#{DiffuseColor.R:X2}{DiffuseColor.G:X2}{DiffuseColor.B:X2}";
            set
            {
                if (_node.Mesh?.Material is not { } material) return;
                if (ColorConverter.ConvertFromString(value) is not Color parsed) return;

                material.DiffuseColor = new Color4(parsed.ScR, parsed.ScG, parsed.ScB);
                OnPropertyChanged(nameof(DiffuseColor));
                OnPropertyChanged();
                _onChanged();
            }
        }

        private static float Clamp01(float value) => System.Math.Clamp(value, 0f, 1f);

        private void ApplyRotation() =>
            _node.LocalRotation = EulerAngles.FromDegrees((float)_rotationX, (float)_rotationY, (float)_rotationZ);

        /// <summary>Re-reads every property from the wrapped <see cref="Node"/> and
        /// raises change notifications for all of them - call after something other than
        /// this view model's own setters changed it (a gizmo drag, most importantly;
        /// also used once at construction). <c>string.Empty</c> as the property name
        /// tells WPF's binding system "assume everything on this object may have
        /// changed", rather than naming each of the dozen properties above individually.</summary>
        public void SyncFromCore()
        {
            var euler = EulerAngles.ToDegrees(_node.LocalRotation);
            _rotationX = euler.X;
            _rotationY = euler.Y;
            _rotationZ = euler.Z;

            OnPropertyChanged(string.Empty);
        }
    }
}
