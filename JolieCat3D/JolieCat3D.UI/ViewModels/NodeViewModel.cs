using System.Collections.ObjectModel;
using System.Numerics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat3D.Core.Constraints;
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

        /// <summary>Every <see cref="NodeViewModel"/> currently in the scene (see
        /// <see cref="SceneViewModel.BuildViewModel"/>'s own remarks) - threaded down to
        /// <see cref="BooleanModifierViewModel"/>'s own target-object picker, the one
        /// modifier type that needs to reference some OTHER node in the scene rather than
        /// only its own mesh/settings.</summary>
        private readonly Func<IEnumerable<NodeViewModel>> _allNodesProvider;

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

        public NodeViewModel(Node node, Action onChanged, Func<IEnumerable<NodeViewModel>> allNodesProvider)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
            _allNodesProvider = allNodesProvider ?? throw new ArgumentNullException(nameof(allNodesProvider));
            SyncFromCore();
            RefreshModifiers();
            RefreshMaterialSlots();
            RefreshCurvePoints();
            RefreshConstraints();
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

        /// <summary>True once this node's material has a normal map assigned (see
        /// <see cref="Core.Materials.Material.NormalTexturePath"/>'s own remarks on why
        /// the live viewport can't actually render it, but the exported glTF can) - the
        /// Properties panel's own "currently loaded normal map" filename label binds its
        /// Visibility to this, mirroring <see cref="HasDiffuseTexture"/>.</summary>
        public bool HasNormalTexture => !string.IsNullOrEmpty(_node.Mesh?.Material?.NormalTexturePath);

        public string NormalTextureFileName =>
            _node.Mesh?.Material?.NormalTexturePath is { } path ? System.IO.Path.GetFileName(path) : string.Empty;

        /// <summary>Points this node's material's normal-map slot at
        /// <paramref name="imagePath"/> - <c>MainWindow</c>'s "Load Normal Map..." button
        /// calls this after its own file-picker dialog returns a path. A no-op if this
        /// node has no mesh/material at all.</summary>
        public void SetNormalTexture(string imagePath)
        {
            if (_node.Mesh?.Material is not { } material) return;

            material.NormalTexturePath = imagePath;

            OnPropertyChanged(nameof(HasNormalTexture));
            OnPropertyChanged(nameof(NormalTextureFileName));
            _onChanged();
        }

        /// <summary>True once this node's material has a packed metallic-roughness map
        /// assigned (see <see cref="Core.Materials.Material.MetallicRoughnessTexturePath"/>'s
        /// own remarks on its channel layout) - mirrors <see cref="HasDiffuseTexture"/>.</summary>
        public bool HasMetallicRoughnessTexture => !string.IsNullOrEmpty(_node.Mesh?.Material?.MetallicRoughnessTexturePath);

        public string MetallicRoughnessTextureFileName =>
            _node.Mesh?.Material?.MetallicRoughnessTexturePath is { } path ? System.IO.Path.GetFileName(path) : string.Empty;

        /// <summary>Points this node's material's metallic-roughness slot at
        /// <paramref name="imagePath"/> - <c>MainWindow</c>'s "Load Metallic/Roughness
        /// Map..." button calls this after its own file-picker dialog returns a path
        /// (the picked image is expected to already be packed roughness-in-green,
        /// metallic-in-blue - see that property's own remarks; this method does no
        /// repacking of its own). A no-op if this node has no mesh/material at all.</summary>
        public void SetMetallicRoughnessTexture(string imagePath)
        {
            if (_node.Mesh?.Material is not { } material) return;

            material.MetallicRoughnessTexturePath = imagePath;

            OnPropertyChanged(nameof(HasMetallicRoughnessTexture));
            OnPropertyChanged(nameof(MetallicRoughnessTextureFileName));
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

        /// <summary>True once this node is actually a curve/spline entity - what the
        /// Curve Inspector section binds its own Visibility to.</summary>
        public bool HasCurve => _node.Curve is not null;

        public bool CurveClosed
        {
            get => _node.Curve?.Closed ?? false;
            set
            {
                if (_node.Curve is not { } curve || curve.Closed == value) return;
                curve.Closed = value;
                OnPropertyChanged();
                RegenerateCurveMesh();
            }
        }

        /// <summary>The task's own "Geometry: Bevel Depth" property - see
        /// <see cref="CurveData.BevelDepth"/>'s own remarks.</summary>
        public float CurveBevelDepth
        {
            get => _node.Curve?.BevelDepth ?? 0.05f;
            set
            {
                if (_node.Curve is not { } curve) return;
                curve.BevelDepth = value;
                OnPropertyChanged();
                RegenerateCurveMesh();
            }
        }

        public int CurveSegmentsPerSpan
        {
            get => _node.Curve?.SegmentsPerSpan ?? 12;
            set
            {
                if (_node.Curve is not { } curve) return;
                curve.SegmentsPerSpan = value;
                OnPropertyChanged();
                RegenerateCurveMesh();
            }
        }

        public int CurveRadialSegments
        {
            get => _node.Curve?.RadialSegments ?? 8;
            set
            {
                if (_node.Curve is not { } curve) return;
                curve.RadialSegments = value;
                OnPropertyChanged();
                RegenerateCurveMesh();
            }
        }

        /// <summary>Mirrors <see cref="CurveData.Points"/> as view models, one per entry -
        /// the Curve Inspector's own points panel binds directly to this. Rebuilt
        /// wholesale by <see cref="RefreshCurvePoints"/>, the same convention
        /// <see cref="Modifiers"/>/<see cref="MaterialSlots"/> already use.</summary>
        public ObservableCollection<CurvePointViewModel> CurvePoints { get; } = new();

        private void RefreshCurvePoints()
        {
            CurvePoints.Clear();
            if (_node.Curve is not { } curve) return;
            foreach (var point in curve.Points)
                CurvePoints.Add(new CurvePointViewModel(point, RegenerateCurveMesh));
        }

        /// <summary>Appends a new control point a fixed offset along +X from the
        /// current last one (the origin if this is the first point at all) - never
        /// coincident with an existing point, so the new span is never degenerate the
        /// instant it's added.</summary>
        public void AddCurvePoint()
        {
            if (_node.Curve is not { } curve) return;

            var last = curve.Points.Count > 0 ? curve.Points[^1].Position : Vector3.Zero;
            curve.Points.Add(new CurvePoint(last + new Vector3(1f, 0f, 0f)));
            RefreshCurvePoints();
            RegenerateCurveMesh();
        }

        /// <summary>Removes <paramref name="point"/> (identified by its own
        /// <see cref="CurvePointViewModel.Underlying"/> reference) from this curve's own
        /// point list - a no-op if it isn't (any longer) actually in it.</summary>
        public void RemoveCurvePoint(CurvePointViewModel point)
        {
            ArgumentNullException.ThrowIfNull(point);
            if (_node.Curve is not { } curve) return;
            if (!curve.Points.Remove(point.Underlying)) return;

            RefreshCurvePoints();
            RegenerateCurveMesh();
        }

        /// <summary>Rebuilds <see cref="Node.Mesh"/> from this curve's own
        /// <see cref="CurveData"/> - see <see cref="CurveData"/>'s own remarks on why
        /// <see cref="Node.Mesh"/> is just a cache here, regenerated (through this SAME
        /// call) after any point/handle/Bevel-Depth/Closed/tessellation edit, going
        /// through the exact same <see cref="_onChanged"/> real-time-render path every
        /// other property edit already uses - no separate rendering path needed for a
        /// curve at all.</summary>
        private void RegenerateCurveMesh()
        {
            if (_node.Curve is not { } curve) return;

            _node.Mesh = curve.GenerateMesh();
            OnPropertyChanged(nameof(HasMesh));
            _onChanged();
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
                    BooleanModifier boolean => new BooleanModifierViewModel(boolean, _onChanged, this, _allNodesProvider),
                    ArrayModifier array => new ArrayModifierViewModel(array, _onChanged),
                    SolidifyModifier solidify => new SolidifyModifierViewModel(solidify, _onChanged),
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

        /// <summary>Appends a new, default-settings (Union, no target picked yet)
        /// <see cref="BooleanModifier"/> to this node's own stack - the Modifiers panel's
        /// "Add Boolean" button. A no-op-looking modifier until its own Target is picked
        /// from the panel's target combo box (see <see cref="BooleanModifierViewModel"/>'s
        /// own remarks) - <see cref="BooleanModifier.Apply"/> already tolerates that
        /// (returns the input mesh completely unchanged), so adding one never breaks the
        /// render before it's actually configured.</summary>
        public void AddBooleanModifier()
        {
            _node.Modifiers.Add(new BooleanModifier());
            RefreshModifiers();
            _onChanged();
        }

        /// <summary>Appends a new, default-settings <see cref="ArrayModifier"/> to this
        /// node's own stack - the Modifiers panel's "Add Array" button.</summary>
        public void AddArrayModifier()
        {
            _node.Modifiers.Add(new ArrayModifier());
            RefreshModifiers();
            _onChanged();
        }

        /// <summary>Appends a new, default-settings <see cref="SolidifyModifier"/> to
        /// this node's own stack - the Modifiers panel's "Add Solidify" button.</summary>
        public void AddSolidifyModifier()
        {
            _node.Modifiers.Add(new SolidifyModifier());
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

        /// <summary>Mirrors <see cref="Node.Constraints"/> as view models, one per entry -
        /// the Constraints panel's own <c>ItemsControl</c> binds directly to this. The
        /// same "rebuilt wholesale, not incrementally patched" convention
        /// <see cref="Modifiers"/> already uses.</summary>
        public ObservableCollection<ConstraintViewModelBase> Constraints { get; } = new();

        private void RefreshConstraints()
        {
            Constraints.Clear();
            foreach (var constraint in _node.Constraints)
            {
                ConstraintViewModelBase? viewModel = constraint switch
                {
                    TrackToConstraint trackTo => new TrackToConstraintViewModel(trackTo, _onChanged, this, _allNodesProvider),
                    _ => null,
                };
                if (viewModel is not null) Constraints.Add(viewModel);
            }
        }

        /// <summary>Appends a new, default-settings (PlusZ forward, no target picked
        /// yet) <see cref="TrackToConstraint"/> to this node's own stack - the
        /// Constraints panel's "Add Track To" button. A no-op-looking constraint until
        /// its own Target is picked from the panel's target combo box (see
        /// <see cref="TrackToConstraintViewModel"/>'s own remarks) - <see cref="TrackToConstraint.Apply"/>
        /// already tolerates that (leaves rotation untouched), so adding one never
        /// disturbs the node's current rotation before it's actually configured.</summary>
        public void AddTrackToConstraint()
        {
            _node.Constraints.Add(new TrackToConstraint());
            RefreshConstraints();
            _onChanged();
        }

        /// <summary>Removes <paramref name="constraint"/> (identified by its own
        /// <see cref="ConstraintViewModelBase.Underlying"/> reference) from this node's
        /// stack - a no-op if it isn't (any longer) actually in it.</summary>
        public void RemoveConstraint(ConstraintViewModelBase constraint)
        {
            ArgumentNullException.ThrowIfNull(constraint);
            if (!_node.Constraints.Remove(constraint.Underlying)) return;

            RefreshConstraints();
            _onChanged();
        }

        /// <summary>Mirrors <see cref="Node.Mesh"/>'s own <see cref="Core.Geometry.Mesh.MaterialSlots"/>
        /// as view models, one per entry, in the same order - Multi-Material Support's
        /// own "Material Slots" list the Material Inspector's <c>ItemsControl</c> binds
        /// to. Rebuilt wholesale by <see cref="RefreshMaterialSlots"/>, the same
        /// "rebuilt, not incrementally patched" convention <see cref="Modifiers"/>
        /// already uses.</summary>
        public ObservableCollection<MaterialSlotViewModel> MaterialSlots { get; } = new();

        private void RefreshMaterialSlots()
        {
            MaterialSlots.Clear();
            if (_node.Mesh is not { } mesh) return;

            for (var i = 0; i < mesh.MaterialSlots.Count; i++)
                MaterialSlots.Add(new MaterialSlotViewModel(mesh, i, _onChanged));
        }

        /// <summary>Appends a new, default-appearance material slot to this node's own
        /// mesh - the Material Inspector's "Add Slot" button. A no-op for a mesh-less
        /// node (nothing to add a slot to).</summary>
        public void AddMaterialSlot()
        {
            if (_node.Mesh is not { } mesh) return;

            mesh.AddMaterialSlot(new Core.Materials.Material($"Slot {mesh.MaterialSlots.Count + 1}"));
            RefreshMaterialSlots();
            _onChanged();
        }

        /// <summary>Removes <paramref name="slot"/> (by its own <see cref="MaterialSlotViewModel.SlotIndex"/>)
        /// from this node's own mesh - see <see cref="Core.Geometry.Mesh.RemoveMaterialSlot"/>'s
        /// own remarks on what happens to any polygon that referenced it (or a LATER
        /// slot). The Material Inspector's own per-row "Remove" button.</summary>
        public void RemoveMaterialSlot(MaterialSlotViewModel slot)
        {
            ArgumentNullException.ThrowIfNull(slot);
            if (_node.Mesh is not { } mesh) return;

            mesh.RemoveMaterialSlot(slot.SlotIndex);
            RefreshMaterialSlots();
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
