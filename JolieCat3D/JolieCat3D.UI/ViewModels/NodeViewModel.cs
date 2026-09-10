using System.Collections.ObjectModel;
using System.Numerics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
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
            RefreshModifiers();
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

        /// <summary>0 (mirror-smooth) to 1 (fully matte) - see <see cref="Core.Materials.Material.Roughness"/>'s
        /// own remarks. 0.5 (the same neutral default <see cref="Core.Materials.Material"/>
        /// itself uses) for a material-less node, so the Material Inspector's slider
        /// still shows a sane position rather than snapping to 0 when nothing is
        /// selected to actually read from.</summary>
        public double Roughness
        {
            get => _node.Mesh?.Material?.Roughness ?? 0.5;
            set
            {
                if (_node.Mesh?.Material is not { } material) return;
                material.Roughness = (float)System.Math.Clamp(value, 0.0, 1.0);
                OnPropertyChanged();
                _onChanged();
            }
        }

        /// <summary>0 (dielectric) to 1 (fully metallic) - see <see cref="Core.Materials.Material.Metallic"/>'s
        /// own remarks.</summary>
        public double Metallic
        {
            get => _node.Mesh?.Material?.Metallic ?? 0.0;
            set
            {
                if (_node.Mesh?.Material is not { } material) return;
                material.Metallic = (float)System.Math.Clamp(value, 0.0, 1.0);
                OnPropertyChanged();
                _onChanged();
            }
        }

        /// <summary>True once this node's material actually has a texture assigned
        /// (<see cref="Core.Materials.Material.DiffuseTexturePath"/> set) - what the
        /// Properties panel binds its "currently loaded texture" filename label's
        /// Visibility to.</summary>
        public bool HasDiffuseTexture => !string.IsNullOrEmpty(_node.Mesh?.Material?.DiffuseTexturePath);

        /// <summary>Just the file name (not the full path - which may be a temp/import
        /// location nobody but this project cares about) of the material's own
        /// <see cref="Core.Materials.Material.DiffuseTexturePath"/>, or an empty string
        /// with none set.</summary>
        public string DiffuseTextureFileName =>
            _node.Mesh?.Material?.DiffuseTexturePath is { } path ? System.IO.Path.GetFileName(path) : string.Empty;

        /// <summary>Points this node's material's diffuse texture slot at
        /// <paramref name="imagePath"/> (see <c>JolieCat3D.Service.Interop.TwoDAssetBridge</c>,
        /// the actual bridge this loads through) sampling the whole image (offset (0,0),
        /// scale (1,1)) - <c>MainWindow</c>'s "Load Texture..." button calls this after
        /// its own file-picker dialog returns a path. A no-op if this node has no
        /// mesh/material to set a texture on at all.</summary>
        public void SetDiffuseTexture(string imagePath)
        {
            if (_node.Mesh?.Material is not { } material) return;

            material.DiffuseTexturePath = imagePath;
            material.DiffuseTextureOffset = System.Numerics.Vector2.Zero;
            material.DiffuseTextureScale = System.Numerics.Vector2.One;

            OnPropertyChanged(nameof(HasDiffuseTexture));
            OnPropertyChanged(nameof(DiffuseTextureFileName));
            _onChanged();
        }

        /// <summary>(Re)maps this node's mesh's own vertex UVs via <paramref name="mode"/>
        /// (see <see cref="UVProjector.Apply"/>) - <c>MainWindow</c>'s Material
        /// Inspector "Planar"/"Box"/"Spherical" buttons call this. A no-op if this node
        /// has no mesh at all.</summary>
        public void ApplyUVProjection(UVProjectionMode mode)
        {
            if (_node.Mesh is not { } mesh) return;

            UVProjector.Apply(mesh, mode);
            _onChanged();
        }

        /// <summary>True once this node actually has a <see cref="Node.Mesh"/> at all -
        /// what the Modifiers panel binds its own Visibility to (a modifier stack on a
        /// pure pivot/grouping node with no mesh would never do anything -
        /// <see cref="Core.Modifiers.ModifierStack.Evaluate"/> is only ever called for a
        /// meshed node in the first place).</summary>
        public bool HasMesh => _node.Mesh is not null;

        /// <summary>True once this node is actually a camera - what the Camera Inspector
        /// section binds its own Visibility to.</summary>
        public bool HasCamera => _node.Camera is not null;

        /// <summary>"Perspective" or "Orthographic" - a plain string (rather than the
        /// enum itself) so a Properties panel <c>ComboBox</c> can bind directly to it
        /// with no <c>IValueConverter</c> of its own. A no-op set on a node with no
        /// <see cref="Node.Camera"/> at all (nothing to change).</summary>
        public string CameraProjectionMode
        {
            get => (_node.Camera?.ProjectionMode ?? Core.Scene.CameraProjectionMode.Perspective).ToString();
            set
            {
                if (_node.Camera is not { } camera) return;
                if (!Enum.TryParse<Core.Scene.CameraProjectionMode>(value, out var mode)) return;

                camera.ProjectionMode = mode;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPerspectiveCamera));
                OnPropertyChanged(nameof(IsOrthographicCamera));
                _onChanged();
            }
        }

        /// <summary>Gates the Field of View field's own Visibility - only meaningful for
        /// a <see cref="Core.Scene.CameraProjectionMode.Perspective"/> camera.</summary>
        public bool IsPerspectiveCamera => _node.Camera?.ProjectionMode != Core.Scene.CameraProjectionMode.Orthographic;

        /// <summary>Gates the Orthographic Width field's own Visibility.</summary>
        public bool IsOrthographicCamera => _node.Camera?.ProjectionMode == Core.Scene.CameraProjectionMode.Orthographic;

        public double CameraFieldOfView
        {
            get => _node.Camera?.FieldOfView ?? 45.0;
            set { if (_node.Camera is not { } camera) return; camera.FieldOfView = (float)value; OnPropertyChanged(); _onChanged(); }
        }

        public double CameraOrthographicWidth
        {
            get => _node.Camera?.OrthographicWidth ?? 10.0;
            set { if (_node.Camera is not { } camera) return; camera.OrthographicWidth = (float)value; OnPropertyChanged(); _onChanged(); }
        }

        public double CameraNearPlaneDistance
        {
            get => _node.Camera?.NearPlaneDistance ?? 0.1;
            set { if (_node.Camera is not { } camera) return; camera.NearPlaneDistance = (float)value; OnPropertyChanged(); _onChanged(); }
        }

        public double CameraFarPlaneDistance
        {
            get => _node.Camera?.FarPlaneDistance ?? 1000.0;
            set { if (_node.Camera is not { } camera) return; camera.FarPlaneDistance = (float)value; OnPropertyChanged(); _onChanged(); }
        }

        /// <summary>True once this node is actually a light - what the Light Inspector
        /// section binds its own Visibility to.</summary>
        public bool HasLight => _node.Light is not null;

        /// <summary>"Directional", "Point", or "Spot" - see <see cref="CameraProjectionMode"/>'s
        /// own remarks on why this is a plain string.</summary>
        public string LightType
        {
            get => (_node.Light?.Type ?? Core.Scene.LightType.Directional).ToString();
            set
            {
                if (_node.Light is not { } light) return;
                if (!Enum.TryParse<Core.Scene.LightType>(value, out var type)) return;

                light.Type = type;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPointOrSpotLight));
                OnPropertyChanged(nameof(IsSpotLight));
                _onChanged();
            }
        }

        /// <summary>Gates the Range field's own Visibility - meaningless for a
        /// <see cref="Core.Scene.LightType.Directional"/> light.</summary>
        public bool IsPointOrSpotLight => _node.Light?.Type is Core.Scene.LightType.Point or Core.Scene.LightType.Spot;

        /// <summary>Gates the Spot Angle field's own Visibility.</summary>
        public bool IsSpotLight => _node.Light?.Type == Core.Scene.LightType.Spot;

        public Color LightColor
        {
            get
            {
                var color = _node.Light?.Color ?? Color4.White;
                return Color.FromScRgb(1f, Clamp01(color.R), Clamp01(color.G), Clamp01(color.B));
            }
            set
            {
                if (_node.Light is not { } light) return;
                light.Color = new Color4(value.ScR, value.ScG, value.ScB);
                OnPropertyChanged();
                OnPropertyChanged(nameof(LightColorHex));
                _onChanged();
            }
        }

        /// <summary>The same color as <see cref="LightColor"/>, as a "#RRGGBB" string -
        /// the same hex-field editing convention <see cref="DiffuseColorHex"/> already
        /// uses.</summary>
        public string LightColorHex
        {
            get => $"#{LightColor.R:X2}{LightColor.G:X2}{LightColor.B:X2}";
            set
            {
                if (_node.Light is not { } light) return;
                if (ColorConverter.ConvertFromString(value) is not Color parsed) return;

                light.Color = new Color4(parsed.ScR, parsed.ScG, parsed.ScB);
                OnPropertyChanged(nameof(LightColor));
                OnPropertyChanged();
                _onChanged();
            }
        }

        public double LightIntensity
        {
            get => _node.Light?.Intensity ?? 1.0;
            set { if (_node.Light is not { } light) return; light.Intensity = (float)System.Math.Max(0.0, value); OnPropertyChanged(); _onChanged(); }
        }

        public double LightRange
        {
            get => _node.Light?.Range ?? 10.0;
            set { if (_node.Light is not { } light) return; light.Range = (float)System.Math.Max(0.0, value); OnPropertyChanged(); _onChanged(); }
        }

        public double LightSpotAngle
        {
            get => _node.Light?.SpotAngle ?? 45.0;
            set { if (_node.Light is not { } light) return; light.SpotAngle = (float)System.Math.Clamp(value, 1.0, 179.0); OnPropertyChanged(); _onChanged(); }
        }

        /// <summary>Mirrors <see cref="Node.Modifiers"/> as view models, one per entry,
        /// in the same order - the Modifiers panel's own <c>ItemsControl</c> binds
        /// directly to this. Rebuilt (not incrementally patched) by
        /// <see cref="RefreshModifiers"/> whenever the underlying list itself changes
        /// (added/removed) - <see cref="ModifierViewModelBase"/>'s own properties (like
        /// <see cref="ModifierViewModelBase.IsEnabled"/>) still update in place for an
        /// existing entry without needing a rebuild.</summary>
        public ObservableCollection<ModifierViewModelBase> Modifiers { get; } = new();

        private void RefreshModifiers()
        {
            Modifiers.Clear();
            foreach (var modifier in _node.Modifiers)
            {
                ModifierViewModelBase? viewModel = modifier switch
                {
                    MirrorModifier mirror => new MirrorModifierViewModel(mirror, _onChanged),
                    SubdivisionSurfaceModifier subsurf => new SubdivisionSurfaceModifierViewModel(subsurf, _onChanged),
                    _ => null,
                };
                if (viewModel is not null) Modifiers.Add(viewModel);
            }
        }

        /// <summary>Appends a new, default-settings <see cref="MirrorModifier"/> to this
        /// node's own stack - the Modifiers panel's "Add Mirror" button.</summary>
        public void AddMirrorModifier()
        {
            _node.Modifiers.Add(new MirrorModifier());
            RefreshModifiers();
            _onChanged();
        }

        /// <summary>Appends a new, default-settings <see cref="SubdivisionSurfaceModifier"/>
        /// to this node's own stack - the Modifiers panel's "Add Subsurf" button.</summary>
        public void AddSubdivisionSurfaceModifier()
        {
            _node.Modifiers.Add(new SubdivisionSurfaceModifier());
            RefreshModifiers();
            _onChanged();
        }

        /// <summary>Removes <paramref name="modifier"/> (identified by its own
        /// <see cref="ModifierViewModelBase.Underlying"/> reference) from this node's
        /// stack - a no-op if it isn't (any longer) actually in it.</summary>
        public void RemoveModifier(ModifierViewModelBase modifier)
        {
            ArgumentNullException.ThrowIfNull(modifier);
            if (!_node.Modifiers.Remove(modifier.Underlying)) return;

            RefreshModifiers();
            _onChanged();
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
