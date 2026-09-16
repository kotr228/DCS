using System.ComponentModel;
using System.Numerics;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Engine.Rendering;
using CoreNode = JolieCat3D.Core.Scene.Node;
using Quaternion = System.Numerics.Quaternion;

namespace JolieCat3D.Engine.Gizmos
{
    /// <summary>One whole drag gesture's own before/after transform, raised by
    /// <see cref="TransformGizmo.TransformCommitted"/> - the Undo/Redo system's own
    /// entry point (<c>Service.Commands.TransformNodeCommand</c>) for turning a
    /// completed drag into one recorded command, as opposed to
    /// <see cref="TransformGizmo.TransformChanged"/>'s per-TICK firing (meant for live
    /// visual feedback while the drag is still happening, not for deciding when a whole
    /// gesture is "done").</summary>
    public sealed class TransformCommittedEventArgs : EventArgs
    {
        public CoreNode Target { get; }
        public (Vector3 Position, Quaternion Rotation, Vector3 Scale) Before { get; }
        public (Vector3 Position, Quaternion Rotation, Vector3 Scale) After { get; }

        public TransformCommittedEventArgs(
            CoreNode target,
            (Vector3 Position, Quaternion Rotation, Vector3 Scale) before,
            (Vector3 Position, Quaternion Rotation, Vector3 Scale) after)
        {
            Target = target;
            Before = before;
            After = after;
        }
    }

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

        /// <summary>Every handle's own REACH (a Translate/Scale arrow's <see cref="TranslateManipulator.Length"/>,
        /// a Rotate ring's own radius) scales with <see cref="Target"/>'s own current
        /// world-space size (see <see cref="GetTargetMaxExtent"/>) rather than a fixed
        /// world-unit constant - a fixed reach either swallowed a small object or, for a
        /// large one, sat so far inside it (or, cut small enough not to, rendered at a
        /// thickness too close to zero to survive WPF's own rasterization) that the handle
        /// effectively disappeared - both are exactly what an earlier round's fixed
        /// Diameter/Length pairs (down to Translate 0.02/0.55, Rotate 1.0/0.93) ran into.
        /// THICKNESS (a Translate/Scale arrow's own <see cref="TranslateManipulator.Diameter"/>,
        /// a Rotate ring's own tube diameter) still stays a small fraction of that same
        /// reach - see <see cref="GetTargetMaxExtent"/>'s own factor constants below - so a
        /// bigger object gets a proportionally longer handle, never a proportionally FATTER
        /// one. <see cref="GizmoHitTester"/> (see <see cref="Rendering.Visual3DExtensions"/>'s
        /// neighbor of the same purpose) is what makes a handle reliably clickable regardless
        /// of how thin it renders, so thinness never trades off against grabbability here.
        /// See <see cref="UpdateHandleSizing"/> for where these factors are actually applied.</summary>
        private const double TranslateLengthFactor = 1.5;
        private const double TranslateDiameterFactor = 0.02;

        /// <summary>Scale mode's own reused <see cref="TranslateManipulator"/> (no dedicated
        /// scale manipulator ships with HelixToolkit.Wpf 2.24.0 - see this class's own
        /// remarks) - a visibly THINNER <see cref="TranslateDiameterFactor"/> is what
        /// distinguishes it from Translate mode at a glance.</summary>
        private const double ScaleLengthFactor = 1.5;
        private const double ScaleDiameterFactor = 0.012;

        /// <summary>The Rotate ring's own centerline radius factor, and its tube's diameter
        /// factor (of <see cref="Target"/>'s own <see cref="GetTargetMaxExtent"/>, not of the
        /// ring's own radius - a fixed fraction of maxExtent keeps the tube visually
        /// consistent with Translate/Scale's own arrow thickness, both being sized off the
        /// very same object-size signal).</summary>
        private const double RotateRadiusFactor = 1.2;
        private const double RotateTubeDiameterFactor = 0.02;

        /// <summary>An absolute floor under every computed Diameter/tube-diameter above -
        /// without one, a sufficiently tiny selected object (or one with no mesh at all;
        /// see <see cref="GetTargetMaxExtent"/>'s own fallback) could compute a thickness so
        /// close to zero it renders as nothing, reproducing this exact "gizmo disappeared"
        /// bug from a different direction. Small enough to still read as "thin" against any
        /// object this project's own primitives are actually authored at (a similar 1-unit
        /// scale - see <see cref="GridSize"/>'s own remarks).</summary>
        private const double MinDiameter = 0.01;

        /// <summary>The world-space size <see cref="Target"/>'s own bounds fall back to when
        /// they collapse to a single point - a node with no mesh (a pure transform/pivot) or
        /// an empty one (see <see cref="CoreNode.GetWorldBounds"/>'s own remarks) - so the
        /// gizmo still shows at a sane, unremarkable size instead of vanishing to
        /// <see cref="MinDiameter"/>-thin nothing. Matches this project's own established
        /// "primitives are authored at a 1-unit scale" convention (<see cref="GridSize"/>'s
        /// own remarks) - the same reasonable size a mesh-bearing node of "typical" size
        /// would compute anyway.</summary>
        private const double DefaultExtent = 1.0;

        private readonly HelixViewport3D _viewport;
        private readonly List<(Manipulator Manipulator, EventHandler ValueChangedHandler)> _activeManipulators = new();

        private GizmoMode _mode = GizmoMode.Translate;

        /// <summary>The manipulators currently shown in the viewport (empty when nothing
        /// is attached/selected) - exposed so <see cref="GizmoHitTester"/> can hit-test a
        /// viewport click against them directly, independent of WPF's own 3D hit-testing
        /// (see that class's own remarks for why that's necessary).</summary>
        public IReadOnlyList<Manipulator> Handles => _activeManipulators.Select(t => t.Manipulator).ToList();

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

        /// <summary>The world-space grid increment a Translate drag snaps to while the
        /// Ctrl key is held (see <see cref="ApplyTranslate"/>'s own remarks) - has no
        /// effect at all unless Ctrl is actually down during the drag, so this can be
        /// freely set (from a UI numeric field) without changing anything about ordinary,
        /// unmodified dragging. 1 world unit by default - this project's own primitives
        /// (<c>Core.Geometry.Primitives</c>) are all authored at a similar 1-unit scale,
        /// so a plain "1" is a reasonable, unsurprising starting increment.</summary>
        public float GridSize { get; set; } = 1f;

        /// <summary>The Translate drag gesture's own running total LOCAL-space offset
        /// since <see cref="Target"/>'s position at the moment the CURRENTLY-captured
        /// handle first grabbed the mouse - null whenever no Translate drag is in
        /// progress. Tracked separately from what's actually written to
        /// <see cref="CoreNode.LocalPosition"/> (see <see cref="ApplyTranslate"/>'s own
        /// remarks on why) so grid-snapping a drag never accumulates rounding drift the
        /// way repeatedly re-snapping an already-snapped value tick after tick would.</summary>
        private Vector3? _translateDragStartPosition;
        private Vector3 _translateAccumulatedDelta;

        /// <summary>Raised after any drag actually changes <see cref="Target"/>'s
        /// transform - the caller's cue to re-render the scene (see
        /// <c>Rendering.Scene3DRenderer.Refresh"/>) and refresh any UI (a Properties
        /// Inspector's Position/Rotation/Scale fields) still showing the old values.
        /// Fires on every intermediate tick of an in-progress drag, for live visual
        /// feedback - see <see cref="TransformCommitted"/> for the "the WHOLE drag
        /// gesture just finished" signal instead.</summary>
        public event EventHandler? TransformChanged;

        /// <summary>Raised exactly once when a WHOLE drag gesture ends (the manipulator
        /// releases mouse capture - see <see cref="TrackDelta"/>), carrying the
        /// before/after transform the entire gesture produced - <c>JolieCat3D.UI</c>'s
        /// own cue to record ONE <c>Service.Commands.TransformNodeCommand</c> for the
        /// whole drag, not one per <see cref="TransformChanged"/> tick (which would turn
        /// a single mouse drag into dozens of individually-undoable micro-steps). Not
        /// raised at all if the drag ended exactly where it started (nothing changed, so
        /// nothing to undo) - see <see cref="TrackDelta"/>'s own comparison.</summary>
        public event EventHandler<TransformCommittedEventArgs>? TransformCommitted;

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
                // A manipulator can be asked to refresh a moment after it's actually been
                // detached from the viewport (see Rebuild()'s own remarks on the
                // mid-drag-Rebuild scenario this guards against) - HelixToolkit.Wpf's own
                // internal handling of a Position/Pivot change on a detached manipulator
                // resolves its own Viewport3D and THROWS rather than returning null once
                // it isn't attached (see Rendering.Visual3DExtensions.GetViewport3DOrNull's
                // own remarks) - skip a detached manipulator entirely rather than crash.
                if (manipulator.GetViewport3DOrNull() is null) continue;

                manipulator.Position = position;
                if (manipulator is RotateManipulator rotateManipulator) rotateManipulator.Pivot = position;
            }

            UpdateHandleSizing();
        }

        private void Rebuild()
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(Manipulator.ValueProperty, typeof(Manipulator));
            foreach (var (manipulator, handler) in _activeManipulators)
            {
                // A manipulator being torn down can still hold an ACTIVE mouse capture -
                // this Rebuild() call itself is reachable reentrantly mid-drag (a
                // selection change, an Edit Mode toggle, or a Properties Inspector edit
                // made from inside one of this gizmo's own TransformChanged/TransformCommitted
                // handlers while a handle is still captured). WPF keeps routing further
                // mouse events to whichever element currently holds capture regardless of
                // whether it's still IN the visual tree, and HelixToolkit.Wpf's own
                // manipulator code resolves its own Viewport3D on every one of those -
                // throwing once the visual is detached (see this class's own remarks on
                // Rendering.Visual3DExtensions.GetViewport3DOrNull). Releasing capture
                // FIRST, before removing the visual, is what stops WPF from ever
                // delivering that further event in the first place.
                if (manipulator.IsMouseCaptured) manipulator.ReleaseMouseCapture();

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

            UpdateHandleSizing();
        }

        /// <summary>Resizes every currently-active handle to match <see cref="Target"/>'s
        /// CURRENT world-space size (see <see cref="GetTargetMaxExtent"/>) - a Translate/Scale
        /// arrow's reach (<see cref="TranslateManipulator.Length"/>) and a Rotate ring's own
        /// radius scale with the object, while every handle's THICKNESS stays a small,
        /// near-constant fraction of that same reach (never large in absolute terms - see
        /// <see cref="MinDiameter"/>'s own remarks for the one floor under it). Called after
        /// <see cref="Rebuild"/> builds/replaces the handle set, and after every
        /// <see cref="Refresh"/> (a drag tick, a Properties Inspector edit, an undo) since
        /// any of those can change <see cref="Target"/>'s own Scale and therefore its
        /// bounds - the handles need to track that live, not just at the moment they were
        /// first built.</summary>
        private void UpdateHandleSizing()
        {
            if (Target is null) return;

            var maxExtent = GetTargetMaxExtent();

            foreach (var (manipulator, _) in _activeManipulators)
            {
                if (manipulator.GetViewport3DOrNull() is null) continue;

                switch (manipulator)
                {
                    case RotateManipulator rotate:
                    {
                        var tubeDiameter = Math.Max(MinDiameter, maxExtent * RotateTubeDiameterFactor);
                        var radius = maxExtent * RotateRadiusFactor;
                        rotate.Diameter = 2 * (radius + tubeDiameter / 2);
                        rotate.InnerDiameter = 2 * (radius - tubeDiameter / 2);
                        break;
                    }

                    case TranslateManipulator translate when Mode == GizmoMode.Translate:
                        translate.Length = maxExtent * TranslateLengthFactor;
                        translate.Diameter = Math.Max(MinDiameter, maxExtent * TranslateDiameterFactor);
                        break;

                    case TranslateManipulator translate when Mode == GizmoMode.Scale:
                        translate.Length = maxExtent * ScaleLengthFactor;
                        translate.Diameter = Math.Max(MinDiameter, maxExtent * ScaleDiameterFactor);
                        break;
                }
            }
        }

        /// <summary>The largest of <see cref="Target"/>'s own world-space bounding-box
        /// dimensions (<see cref="CoreNode.GetWorldBounds"/>) - <see cref="DefaultExtent"/>
        /// for a mesh-less node (or one whose mesh is empty), whose bounds collapse to a
        /// single point (that method's own documented behavior) and so would otherwise
        /// compute a zero reach/thickness, reproducing this very "gizmo disappeared" bug
        /// for that case specifically.</summary>
        private double GetTargetMaxExtent()
        {
            var (min, max) = Target!.GetWorldBounds();
            var size = max - min;
            var maxExtent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
            return maxExtent > 1e-4f ? maxExtent : DefaultExtent;
        }

        private void AddTranslateHandle(Point3D position, Vector3 worldAxis, Vector3D direction, Color color)
        {
            // Diameter/Length are placeholders, immediately overwritten by UpdateHandleSizing
            // (called at the end of the very same Rebuild() this method's own caller is
            // inside) once Target's actual bounds are known - never what's actually rendered.
            var manipulator = new TranslateManipulator
            {
                Position = position,
                Direction = direction,
                Diameter = MinDiameter,
                Length = DefaultExtent,
                Color = color,
            };

            // A SEPARATE GotMouseCapture/LostMouseCapture pair from the one TrackDelta
            // already subscribes for TransformCommitted's own before/after snapshot -
            // WPF routed events support multiple independent subscribers on the same
            // element, and these two do genuinely independent things (one tracks
            // Undo/Redo's own before-state, this one tracks grid-snapping's own running
            // total - see ApplyTranslate's own remarks), so there is no need to fold this
            // into TrackDelta itself, which Rotate/Scale handles share and neither of
            // which grid-snaps at all.
            manipulator.GotMouseCapture += (_, _) =>
            {
                _translateDragStartPosition = Target?.LocalPosition;
                _translateAccumulatedDelta = Vector3.Zero;
            };
            manipulator.LostMouseCapture += (_, _) =>
            {
                _translateDragStartPosition = null;
                _translateAccumulatedDelta = Vector3.Zero;
            };

            TrackDelta(manipulator, delta => ApplyTranslate(worldAxis, delta));
        }

        private void AddRotateHandle(Point3D position, Vector3 worldAxis, Vector3D axis, Color color)
        {
            // See AddTranslateHandle's own remarks - Diameter/InnerDiameter here are likewise
            // just-built placeholders, overwritten by the same end-of-Rebuild() call.
            var manipulator = new RotateManipulator
            {
                Position = position,
                Pivot = position,
                Axis = axis,
                Diameter = DefaultExtent,
                InnerDiameter = DefaultExtent - MinDiameter,
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
            // more than one gizmo at once. Diameter/Length here are likewise just-built
            // placeholders - see AddTranslateHandle's own remarks.
            var manipulator = new TranslateManipulator
            {
                Position = position,
                Direction = direction,
                Diameter = MinDiameter,
                Length = DefaultExtent,
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

            // GotMouseCapture/LostMouseCapture bracket exactly one whole drag gesture -
            // the standard WPF convention every mouse-driven manipulator relies on for
            // its own dragging (capture the mouse on press, release it on release), used
            // here purely to know WHEN one whole gesture starts/ends for
            // TransformCommitted's own sake, not to drive the drag itself (TrackDelta's
            // own Value-changed handler above still does that, exactly as before).
            // Plain CLR event subscriptions, unlike the DependencyPropertyDescriptor one
            // above - no explicit unsubscription needed in Rebuild(): once a discarded
            // manipulator has no other reference keeping it alive, it (and these two
            // handlers along with it) become garbage-collectible together regardless.
            (Vector3 Position, Quaternion Rotation, Vector3 Scale)? dragStartState = null;

            manipulator.GotMouseCapture += (_, _) =>
            {
                if (Target is { } node) dragStartState = (node.LocalPosition, node.LocalRotation, node.LocalScale);
            };

            manipulator.LostMouseCapture += (_, _) =>
            {
                if (Target is { } node && dragStartState is { } before)
                {
                    var after = (node.LocalPosition, node.LocalRotation, node.LocalScale);
                    if (before.Position != after.LocalPosition || before.Rotation != after.LocalRotation || before.Scale != after.LocalScale)
                        TransformCommitted?.Invoke(this, new TransformCommittedEventArgs(node, before, after));
                }

                dragStartState = null;
            };

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
        /// space, not some skewed amount.
        ///
        /// While the Ctrl key is held (checked fresh on every tick, via
        /// <see cref="Keyboard.Modifiers"/> - so toggling it mid-drag takes effect
        /// immediately, not just at the moment the drag started), the position actually
        /// written to <see cref="CoreNode.LocalPosition"/> is grid-snapped
        /// (<see cref="GridSnapping.Snap"/>) instead of applied raw. Snapping is done
        /// against <see cref="_translateAccumulatedDelta"/> - this WHOLE drag gesture's
        /// own running total offset from <see cref="_translateDragStartPosition"/>, not
        /// each tiny per-tick delta individually - and the result is written as an
        /// ABSOLUTE new position (<c>start + snap(total)</c>), never accumulated onto the
        /// previous tick's already-written value: repeatedly re-snapping an
        /// already-snapped running position tick after tick would silently drift off the
        /// true grid over a long drag (each small unsnapped sub-grid remainder getting
        /// rounded away again and again); snapping the same fixed starting point's own
        /// total offset fresh every tick cannot drift, no matter how many ticks the drag
        /// produces.</summary>
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

            if (_translateDragStartPosition is { } startPosition)
            {
                _translateAccumulatedDelta += localDelta;
                var snapRequested = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                node.LocalPosition = GridSnapping.ComputeSnappedPosition(startPosition, _translateAccumulatedDelta, snapRequested, GridSize);
            }
            else
            {
                // No drag-start snapshot on record (GotMouseCapture always fires before
                // any Value-changed tick in the ordinary case - this is only reachable if
                // something applies a delta outside a real mouse-driven drag) - fall back
                // to the plain incremental behavior rather than silently doing nothing.
                node.LocalPosition += localDelta;
            }

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
