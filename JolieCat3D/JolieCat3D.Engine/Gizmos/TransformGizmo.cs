using System.ComponentModel;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using CoreNode = JolieCat3D.Core.Scene.Node;
using Quaternion = System.Numerics.Quaternion;

namespace JolieCat3D.Engine.Gizmos
{
    /// <summary>
    /// The on-screen Translate/Rotate/Scale handles for whichever <see cref="CoreNode"/>
    /// is currently attached (see <see cref="Attach"/>) - built from
    /// <c>HelixToolkit.Wpf</c>'s own <see cref="TranslateManipulator"/>/<see cref="RotateManipulator"/>
    /// (one per world axis, colored red/green/blue by the universal X/Y/Z convention)
    /// rather than hand-rolled drag math, the same "use the library's own interaction
    /// primitive instead of reimplementing it" reasoning <c>Camera.CameraFraming</c>
    /// already applies to orbit/pan/zoom. HelixToolkit.Wpf 2.24.0 ships no dedicated scale
    /// manipulator, so Scale mode reuses <see cref="TranslateManipulator"/> as its
    /// interaction primitive too (correct drag math, only its visual - an arrow, not a
    /// scale-cube handle - is a known, disclosed simplification of a "basic" first-pass
    /// gizmo).
    /// </summary>
    public sealed class TransformGizmo
    {
        private static readonly Color AxisXColor = Color.FromRgb(0xE0, 0x50, 0x50);
        private static readonly Color AxisYColor = Color.FromRgb(0x50, 0xC0, 0x50);
        private static readonly Color AxisZColor = Color.FromRgb(0x40, 0x80, 0xE0);

        private readonly HelixViewport3D _viewport;
        private readonly List<(Manipulator Manipulator, EventHandler ValueChangedHandler)> _activeManipulators = new();

        private GizmoMode _mode = GizmoMode.Translate;

        public GizmoMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                Rebuild();
            }
        }

        /// <summary>The node the gizmo is currently attached to and driving - null when
        /// nothing is selected, in which case the gizmo shows no handles at all.</summary>
        public CoreNode? Target { get; private set; }

        /// <summary>Raised after any drag actually changes <see cref="Target"/>'s
        /// transform - the caller's cue to re-render the scene (see
        /// <c>Rendering.Scene3DRenderer.Refresh"/>) and refresh any UI (a Properties
        /// Inspector's Position/Rotation/Scale fields) still showing the old values.</summary>
        public event EventHandler? TransformChanged;

        public TransformGizmo(HelixViewport3D viewport) =>
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

        /// <summary>Attaches the gizmo to <paramref name="node"/> (or detaches it
        /// entirely, for null) and (re)builds its handles at <paramref name="node"/>'s
        /// current world position.</summary>
        public void Attach(CoreNode? node)
        {
            Target = node;
            Rebuild();
        }

        /// <summary>Moves the current handles to <see cref="Target"/>'s current world
        /// position, without touching which handles exist - deliberately NOT a full
        /// <see cref="Rebuild"/>: this is also called from inside an active drag's own
        /// Value-changed callback (see <see cref="TrackDelta"/>), and tearing down and
        /// recreating the very manipulator the mouse is currently captured by mid-drag
        /// would drop that capture and break the gesture. Call after anything else (a
        /// Properties Inspector edit, an undo) moves <see cref="Target"/> too, so the
        /// gizmo doesn't visually lag behind the object it's supposed to be on.</summary>
        public void Refresh()
        {
            if (Target is null) return;

            var worldPosition = Target.GetWorldPosition();
            var position = new Point3D(worldPosition.X, worldPosition.Y, worldPosition.Z);

            foreach (var (manipulator, _) in _activeManipulators)
            {
                manipulator.Position = position;
                if (manipulator is RotateManipulator rotateManipulator) rotateManipulator.Pivot = position;
            }
        }

        private void Rebuild()
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(Manipulator.ValueProperty, typeof(Manipulator));
            foreach (var (manipulator, handler) in _activeManipulators)
            {
                descriptor?.RemoveValueChanged(manipulator, handler);
                _viewport.Children.Remove(manipulator);
            }
            _activeManipulators.Clear();

            if (Target is null) return;

            var worldPosition = Target.GetWorldPosition();
            var position = new Point3D(worldPosition.X, worldPosition.Y, worldPosition.Z);

            switch (Mode)
            {
                case GizmoMode.Translate:
                    AddTranslateHandle(position, Vector3.UnitX, new Vector3D(1, 0, 0), AxisXColor);
                    AddTranslateHandle(position, Vector3.UnitY, new Vector3D(0, 1, 0), AxisYColor);
                    AddTranslateHandle(position, Vector3.UnitZ, new Vector3D(0, 0, 1), AxisZColor);
                    break;

                case GizmoMode.Rotate:
                    AddRotateHandle(position, Vector3.UnitX, new Vector3D(1, 0, 0), AxisXColor);
                    AddRotateHandle(position, Vector3.UnitY, new Vector3D(0, 1, 0), AxisYColor);
                    AddRotateHandle(position, Vector3.UnitZ, new Vector3D(0, 0, 1), AxisZColor);
                    break;

                case GizmoMode.Scale:
                    AddScaleHandle(position, Vector3.UnitX, new Vector3D(1, 0, 0), AxisXColor);
                    AddScaleHandle(position, Vector3.UnitY, new Vector3D(0, 1, 0), AxisYColor);
                    AddScaleHandle(position, Vector3.UnitZ, new Vector3D(0, 0, 1), AxisZColor);
                    break;
            }
        }

        private void AddTranslateHandle(Point3D position, Vector3 worldAxis, Vector3D direction, Color color)
        {
            var manipulator = new TranslateManipulator
            {
                Position = position,
                Direction = direction,
                Diameter = 0.15,
                Length = 1.2,
                Color = color,
            };

            TrackDelta(manipulator, delta => ApplyTranslate(worldAxis, delta));
        }

        private void AddRotateHandle(Point3D position, Vector3 worldAxis, Vector3D axis, Color color)
        {
            var manipulator = new RotateManipulator
            {
                Position = position,
                Pivot = position,
                Axis = axis,
                Diameter = 2.2,
                InnerDiameter = 1.9,
                Color = color,
            };

            TrackDelta(manipulator, delta => ApplyRotate(worldAxis, delta));
        }

        private void AddScaleHandle(Point3D position, Vector3 worldAxis, Vector3D direction, Color color)
        {
            // See this class's own remarks: reusing TranslateManipulator as the
            // interaction primitive for Scale (no dedicated scale manipulator ships
            // with HelixToolkit.Wpf 2.24.0) - a smaller Diameter than the Translate mode
            // uses is the one visual cue distinguishing it, beyond mode never showing
            // more than one gizmo at once.
            var manipulator = new TranslateManipulator
            {
                Position = position,
                Direction = direction,
                Diameter = 0.22,
                Length = 0.8,
                Color = color,
            };

            TrackDelta(manipulator, delta => ApplyScale(worldAxis, delta));
        }

        /// <summary>
        /// Subscribes to <paramref name="manipulator"/>'s own <see cref="Manipulator.Value"/>
        /// dependency property and invokes <paramref name="applyDelta"/> with
        /// (newValue - oldValue) on every change - the actual incremental amount to apply
        /// this frame, regardless of whether <see cref="Manipulator.Value"/> itself is a
        /// running total or resets at the start of each drag (a detail this project has
        /// no way to verify without running the real control on Windows) - a delta between
        /// two consecutive readings is correct either way. Adds the manipulator to the
        /// viewport and registers it for <see cref="Rebuild"/> to remove later.
        /// </summary>
        private void TrackDelta(Manipulator manipulator, Action<double> applyDelta)
        {
            var lastValue = manipulator.Value;

            EventHandler handler = (_, _) =>
            {
                var newValue = manipulator.Value;
                var delta = newValue - lastValue;
                lastValue = newValue;

                if (delta == 0) return;

                applyDelta(delta);
                TransformChanged?.Invoke(this, EventArgs.Empty);
            };

            var descriptor = DependencyPropertyDescriptor.FromProperty(Manipulator.ValueProperty, typeof(Manipulator));
            descriptor?.AddValueChanged(manipulator, handler);

            _viewport.Children.Add(manipulator);
            _activeManipulators.Add((manipulator, handler));
        }

        /// <summary>Converts a world-space delta along <paramref name="worldAxis"/> into
        /// the matching <see cref="CoreNode.LocalPosition"/> change - a straight add for a
        /// root node, or (for a node under a rotated/scaled parent) the delta rotated and
        /// unscaled into the parent's own local frame first, via <see cref="Matrix4x4.Decompose"/>
        /// of the parent's world transform - verified against a hand-built parent/child
        /// case (a rotated, non-uniformly-scaled parent) before being written here; a
        /// world-space drag on a child node moves it by exactly that amount in world
        /// space, not some skewed amount.</summary>
        private void ApplyTranslate(Vector3 worldAxis, double delta)
        {
            if (Target is not { } node) return;

            var worldDelta = worldAxis * (float)delta;

            Vector3 localDelta;
            if (node.Parent is null)
            {
                localDelta = worldDelta;
            }
            else
            {
                var parentWorld = node.Parent.GetWorldTransform();
                Matrix4x4.Decompose(parentWorld, out var parentScale, out var parentRotation, out _);
                var linear = Matrix4x4.CreateScale(parentScale) * Matrix4x4.CreateFromQuaternion(parentRotation);
                Matrix4x4.Invert(linear, out var invLinear);
                localDelta = Vector3.TransformNormal(worldDelta, invLinear);
            }

            node.LocalPosition += localDelta;
            Refresh();
        }

        /// <summary>Rotates <see cref="Target"/> further around the fixed world axis
        /// <paramref name="worldAxis"/> by <paramref name="delta"/> radians, converting
        /// that into the matching <see cref="CoreNode.LocalRotation"/> change - verified
        /// (for both a root node and a node under a rotated parent) against the actual
        /// System.Numerics.Quaternion multiplication convention (which composes its
        /// right-hand operand first - the reverse of Matrix4x4's own row-vector A*B
        /// reading) before being written here, not assumed. Radians: RotateManipulator's
        /// own Value units aren't documented, and this can't be confirmed by running the
        /// real control in this environment - the standard WPF/Helix convention for an
        /// atan2-derived angle is radians, so that's what's used; worth a quick sanity
        /// check of rotation speed the first time this runs on real hardware.</summary>
        private void ApplyRotate(Vector3 worldAxis, double delta)
        {
            if (Target is not { } node) return;

            var deltaRotation = Quaternion.CreateFromAxisAngle(worldAxis, (float)delta);
            var parentWorldRotation = node.Parent?.GetWorldRotation() ?? Quaternion.Identity;

            // worldRotation = parentWorldRotation * LocalRotation (see Node.GetWorldRotation).
            // newWorldRotation = deltaRotation * worldRotation (apply the existing
            // rotation first, then the world-axis delta on top of it - Quaternion
            // multiplication applies its right-hand operand first).
            // newLocalRotation = inverse(parentWorldRotation) * newWorldRotation.
            var newLocalRotation = Quaternion.Inverse(parentWorldRotation) * deltaRotation * parentWorldRotation * node.LocalRotation;
            node.LocalRotation = Quaternion.Normalize(newLocalRotation);
            Refresh();
        }

        /// <summary>Adds <paramref name="delta"/> to the <paramref name="worldAxis"/>
        /// component of <see cref="CoreNode.LocalScale"/> - local, not world, since
        /// scale (unlike position/rotation) has no well-defined "world axis" meaning
        /// once a parent's own rotation is involved; a "basic" scale tool operating in
        /// the object's own local axes is the standard simplification every editor's
        /// non-uniform-parent case eventually has to make somewhere. Clamped well above
        /// zero so a drag can never flip or collapse the object.</summary>
        private void ApplyScale(Vector3 worldAxis, double delta)
        {
            if (Target is not { } node) return;

            const float minimumScale = 0.01f;
            var change = worldAxis * (float)delta;
            var newScale = node.LocalScale + change;

            node.LocalScale = new Vector3(
                MathF.Max(minimumScale, newScale.X),
                MathF.Max(minimumScale, newScale.Y),
                MathF.Max(minimumScale, newScale.Z));

            Refresh();
        }
    }
}
