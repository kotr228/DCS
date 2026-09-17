using System.ComponentModel;
using System.Numerics;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Engine.Rendering;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreRay = JolieCat3D.Core.Geometry.Ray;
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

        private TransformSpace _space = TransformSpace.Global;

        /// <summary>Which axes Translate/Rotate currently show/drag along - see
        /// <see cref="TransformSpace"/>'s own remarks. Changing this rebuilds the handles
        /// at their new orientation immediately, the same "setting the mode applies it too"
        /// shape <see cref="Mode"/>'s own setter already has. Scale is unaffected either way
        /// (see <see cref="GetActiveAxes"/>'s own remarks on why).</summary>
        public TransformSpace Space
        {
            get => _space;
            set
            {
                if (_space == value) return;
                _space = value;
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

        /// <summary>Every node CURRENTLY in the scene - a live delegate, not a one-time
        /// snapshot (the same "always reflects what's there right now" shape
        /// <c>UI.ViewModels.SceneViewModel</c>'s own <c>AllNodes</c> provider already
        /// established for exactly this kind of cross-cutting need), what Vertex/Edge
        /// Snapping (see <see cref="TryFindVertexEdgeSnapPoint"/>) raycasts against
        /// (excluding <see cref="Target"/> itself). Null (the default) disables
        /// Vertex/Edge Snapping entirely - a Shift-held Translate drag simply behaves as
        /// an unmodified one, rather than throwing for a caller that never wires this
        /// up.</summary>
        public Func<IEnumerable<CoreNode>>? SceneNodes { get; set; }

        /// <summary>"Affect Only: Origin" - off by default. While on, every
        /// Translate/Rotate/Scale drag still writes to <see cref="Target"/>'s own
        /// <see cref="CoreNode.LocalPosition"/>/<see cref="CoreNode.LocalRotation"/>/
        /// <see cref="CoreNode.LocalScale"/> exactly as it always does (moving the
        /// gizmo/pivot itself), but <see cref="CoreNode.CompensateMeshForOriginChange"/>
        /// immediately bakes the inverse of that same change into the mesh's own
        /// vertices too - so the OBJECT never visibly moves/rotates/rescales in the
        /// viewport, only its own origin/pivot does. The standard "move the pivot to a
        /// hinge/joint without disturbing the geometry already modeled around it"
        /// professional-tool feature this project's own object-mode gizmo didn't have a
        /// way to do at all before this existed.</summary>
        public bool AffectOnlyOrigin { get; set; }

        /// <summary>Align to Surface - off by default. While on, whenever a Shift-held
        /// Translate drag actually lands on Face Snapping (see <see cref="TryFindFaceSnapHit"/> -
        /// only reachable when nothing closer qualifies for Vertex/Edge Snapping first,
        /// same priority order as everything else Shift does), <see cref="Target"/>'s
        /// own rotation is ALSO overwritten so its local +Z axis ("Up", per the task's
        /// own wording - not this project's usual scene-wide +Y "up" convention, a
        /// deliberate, disclosed exception scoped to just this one feature) points
        /// exactly along the snapped-onto face's own normal - see
        /// <see cref="ApplyAlignToSurface"/>. Has no effect at all unless a drag
        /// actually reaches Face Snapping (Shift held AND the cursor is over some other
        /// mesh's geometry with no closer vertex/edge) - toggling it with no such drag
        /// in progress changes nothing.</summary>
        public bool AlignToSurfaceEnabled { get; set; }

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
            var (xAxis, yAxis, zAxis) = GetActiveAxes();

            switch (Mode)
            {
                case GizmoMode.Translate:
                    AddTranslateHandle(position, xAxis, ToDirection(xAxis), AxisXColor);
                    AddTranslateHandle(position, yAxis, ToDirection(yAxis), AxisYColor);
                    AddTranslateHandle(position, zAxis, ToDirection(zAxis), AxisZColor);
                    break;

                case GizmoMode.Rotate:
                    AddRotateHandle(position, xAxis, ToDirection(xAxis), AxisXColor);
                    AddRotateHandle(position, yAxis, ToDirection(yAxis), AxisYColor);
                    AddRotateHandle(position, zAxis, ToDirection(zAxis), AxisZColor);
                    break;

                case GizmoMode.Scale:
                    AddScaleHandle(position, xAxis, ToDirection(xAxis), AxisXColor);
                    AddScaleHandle(position, yAxis, ToDirection(yAxis), AxisYColor);
                    AddScaleHandle(position, zAxis, ToDirection(zAxis), AxisZColor);
                    break;
            }

            UpdateHandleSizing();
        }

        /// <summary>The world-space X/Y/Z axes handles are actually built along for the
        /// CURRENT <see cref="Mode"/>/<see cref="Space"/> - plain world unit axes for
        /// Translate/Rotate in <see cref="TransformSpace.Global"/>, or <see cref="Target"/>'s
        /// own current world-rotated local axes (see <see cref="GetLocalAxes"/>) for
        /// Translate/Rotate in <see cref="TransformSpace.Local"/>. Scale ALWAYS uses the
        /// local axes regardless of <see cref="Space"/>: <see cref="ApplyScale"/> writes
        /// straight onto <see cref="CoreNode.LocalScale"/>'s own X/Y/Z components (see its
        /// own remarks - scale has no well-defined WORLD-axis meaning once a parent's
        /// rotation is involved), which only produces a sane, non-shearing result when the
        /// axis handed to it already corresponds to one of the object's own local
        /// components - a genuine world axis would scale a rotated object along a direction
        /// that doesn't line up with any of <see cref="CoreNode.LocalScale"/>'s own axes at
        /// all, distorting rather than scaling it.
        ///
        /// Recomputed once per <see cref="Rebuild"/> (attach, a Mode/Space change) - not
        /// continuously live during a drag. For Translate this is always correct regardless
        /// (a Translate drag never changes <see cref="Target"/>'s own rotation, so its axes
        /// can't go stale mid-drag). For Rotate this is actually the CORRECT behavior, not
        /// merely a simplification: "rotate around my object's local X axis" has to mean one
        /// FIXED world-space direction for the whole gesture, or the rotation itself would
        /// have no consistent axis to turn around at all. The one disclosed limitation this
        /// does leave: after a Local-space Rotate drag changes <see cref="Target"/>'s own
        /// rotation, the gizmo's displayed axes reflect the PRE-drag orientation until the
        /// next <see cref="Rebuild"/> (a new selection, or toggling Mode/Space) - a stale
        /// visual, not an incorrect drag, and not addressed here.</summary>
        private (Vector3 X, Vector3 Y, Vector3 Z) GetActiveAxes() =>
            Space == TransformSpace.Local || Mode == GizmoMode.Scale ? GetLocalAxes() : (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

        /// <summary><see cref="Target"/>'s own current world rotation (<see cref="CoreNode.GetWorldRotation"/>)
        /// applied to the plain X/Y/Z unit axes - "my own local axes, expressed in world
        /// space", the same rotation <see cref="ApplyRotate"/>/<see cref="ApplyTranslate"/>
        /// already treat any <c>worldAxis</c> they're given as living in, so passing one of
        /// these instead of a plain world unit axis needs no other change to either method
        /// at all: both already just convert "a world-space axis" into the matching
        /// <see cref="CoreNode.LocalPosition"/>/<see cref="CoreNode.LocalRotation"/> change,
        /// with no assumption baked in about which particular world-space direction that
        /// axis actually points.</summary>
        private (Vector3 X, Vector3 Y, Vector3 Z) GetLocalAxes()
        {
            if (Target is null) return (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

            var rotation = Target.GetWorldRotation();
            return (
                Vector3.Transform(Vector3.UnitX, rotation),
                Vector3.Transform(Vector3.UnitY, rotation),
                Vector3.Transform(Vector3.UnitZ, rotation));
        }

        private static Vector3D ToDirection(Vector3 axis) => new(axis.X, axis.Y, axis.Z);

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

            var previousLocalTransform = AffectOnlyOrigin ? node.GetLocalTransform() : default;
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

                // Vertex/Edge Snapping (see TryFindVertexEdgeSnapPoint's own remarks)
                // takes priority over the plain axis-constrained drag entirely for this
                // tick while Shift is held and the cursor is actually over some other
                // mesh's geometry: the object plants exactly on the nearest vertex/edge
                // the cursor is pointing at, not merely somewhere along the dragged
                // axis. _translateAccumulatedDelta is kept in sync with whatever
                // actually gets written below so releasing Shift (or aiming off any
                // geometry) resumes the plain axis-constrained/grid-snapped behavior
                // from wherever the snap left the object, rather than jumping back to
                // wherever the un-snapped running total alone would have placed it.
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && TryFindVertexEdgeSnapPoint() is { } snapWorldPoint)
                {
                    var snappedLocalPosition = ConvertWorldPointToLocal(node, snapWorldPoint);
                    node.LocalPosition = snappedLocalPosition;
                    _translateAccumulatedDelta = snappedLocalPosition - startPosition;
                }
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && TryFindFaceSnapHit() is { } faceHit)
                {
                    // Face Snapping - only reached when no closer vertex/edge qualified
                    // above (see TryFindVertexEdgeSnapPoint's own priority remarks):
                    // plants the object at the EXACT point the cursor's ray crosses the
                    // target face, the task's own "snap the dragged object to the exact
                    // point of intersection on the target face" ask.
                    var snappedLocalPosition = ConvertWorldPointToLocal(node, faceHit.Point);
                    node.LocalPosition = snappedLocalPosition;
                    _translateAccumulatedDelta = snappedLocalPosition - startPosition;

                    if (AlignToSurfaceEnabled) ApplyAlignToSurface(node, faceHit.Normal);
                }
                else
                {
                    var snapRequested = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                    node.LocalPosition = GridSnapping.ComputeSnappedPosition(startPosition, _translateAccumulatedDelta, snapRequested, GridSize);
                }
            }
            else
            {
                // No drag-start snapshot on record (GotMouseCapture always fires before
                // any Value-changed tick in the ordinary case - this is only reachable if
                // something applies a delta outside a real mouse-driven drag) - fall back
                // to the plain incremental behavior rather than silently doing nothing.
                node.LocalPosition += localDelta;
            }

            if (AffectOnlyOrigin) node.CompensateMeshForOriginChange(previousLocalTransform);
            Refresh();
        }

        /// <summary>Converts <paramref name="worldPoint"/> into <paramref name="node"/>'s
        /// own PARENT-relative local space - a straight pass-through for a root node,
        /// or (for a node under a parent) the point re-expressed via the inverse of that
        /// parent's own world transform, the same "world -> owner/parent-local"
        /// conversion this project already established for
        /// <c>Core.Modifiers.BooleanModifier.Apply</c> and
        /// <c>Rendering.Scene3DRenderer.OnPilotedCameraChanged</c>.</summary>
        private static Vector3 ConvertWorldPointToLocal(CoreNode node, Vector3 worldPoint)
        {
            if (node.Parent is null) return worldPoint;
            if (!Matrix4x4.Invert(node.Parent.GetWorldTransform(), out var parentWorldToLocal)) return node.LocalPosition;
            return Vector3.Transform(worldPoint, parentWorldToLocal);
        }

        /// <summary>Vertex/Edge Snapping's own raycast: casts a ray from the viewport's
        /// CURRENT mouse cursor position - read fresh here via
        /// <see cref="Mouse.GetPosition(IInputElement)"/> rather than tracked through a
        /// separate MouseMove subscription, since this method's only caller
        /// (<see cref="ApplyTranslate"/>, itself only ever invoked from a manipulator's
        /// own Value-changed callback) already runs synchronously as part of the very
        /// same mouse-move that's driving the drag, so the cursor position read here is
        /// always the current one with no cross-event-ordering assumptions needed at
        /// all - against every OTHER meshed node currently in <see cref="SceneNodes"/>
        /// (evaluated through its own Modifier stack - see
        /// <see cref="ModifierStack.Evaluate"/> - the same DISPLAYED geometry the
        /// viewport itself renders, not necessarily <see cref="Target"/>'s raw base
        /// mesh). See <see cref="VertexEdgeSnapping.FindNearestVertexOrEdge"/> for the
        /// actual snap math. Null with no <see cref="SceneNodes"/> provider set, no
        /// <see cref="Target"/>, nothing hit, or on ANY unexpected exception from the
        /// WPF-side ray conversion - a live mouse-drag calculation is not a place to let
        /// a rare edge case throw and take the whole drag (and, by extension, the whole
        /// app) down with it.</summary>
        private Vector3? TryFindVertexEdgeSnapPoint()
        {
            if (Target is not { } target) return null;
            if (SceneNodes is not { } sceneNodesProvider) return null;

            try
            {
                var viewport3D = _viewport.Viewport;
                var cursor = Mouse.GetPosition(viewport3D);
                var ray3D = Viewport3DHelper.Point2DtoRay3D(viewport3D, cursor);
                var ray = new CoreRay(ToVector3(ray3D.Origin), ToVector3(ray3D.Direction));

                var triangles = new List<(Vector3 A, Vector3 B, Vector3 C)>();
                foreach (var candidate in sceneNodesProvider())
                {
                    if (candidate == target || candidate.Mesh is not { } mesh) continue;

                    var evaluatedMesh = ModifierStack.Evaluate(mesh, candidate.Modifiers, candidate);
                    var world = candidate.GetWorldTransform();

                    foreach (var face in evaluatedMesh.GetRenderFaces())
                    {
                        triangles.Add((
                            Vector3.Transform(evaluatedMesh.Vertices[face.A].Position, world),
                            Vector3.Transform(evaluatedMesh.Vertices[face.B].Position, world),
                            Vector3.Transform(evaluatedMesh.Vertices[face.C].Position, world)));
                    }
                }

                return VertexEdgeSnapping.FindNearestVertexOrEdge(ray, triangles);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Face Snapping's own raycast - the exact same "cast a ray from the
        /// viewport's current cursor position against every OTHER meshed node's
        /// currently-DISPLAYED geometry" shape <see cref="TryFindVertexEdgeSnapPoint"/>
        /// already uses (see its own remarks - reading the cursor fresh, the
        /// Modifier-stack-evaluated geometry, the try/catch around the WPF-side ray
        /// conversion, all identical), just handed to <see cref="Geometry.FaceSnapping.FindNearestFaceHit"/>
        /// instead of <see cref="VertexEdgeSnapping.FindNearestVertexOrEdge"/> - the
        /// exact intersection point AND that face's own world-space normal (what
        /// <see cref="ApplyAlignToSurface"/> needs).</summary>
        private (Vector3 Point, Vector3 Normal)? TryFindFaceSnapHit()
        {
            if (Target is not { } target) return null;
            if (SceneNodes is not { } sceneNodesProvider) return null;

            try
            {
                var viewport3D = _viewport.Viewport;
                var cursor = Mouse.GetPosition(viewport3D);
                var ray3D = Viewport3DHelper.Point2DtoRay3D(viewport3D, cursor);
                var ray = new CoreRay(ToVector3(ray3D.Origin), ToVector3(ray3D.Direction));

                var triangles = new List<(Vector3 A, Vector3 B, Vector3 C)>();
                foreach (var candidate in sceneNodesProvider())
                {
                    if (candidate == target || candidate.Mesh is not { } mesh) continue;

                    var evaluatedMesh = ModifierStack.Evaluate(mesh, candidate.Modifiers, candidate);
                    var world = candidate.GetWorldTransform();

                    foreach (var face in evaluatedMesh.GetRenderFaces())
                    {
                        triangles.Add((
                            Vector3.Transform(evaluatedMesh.Vertices[face.A].Position, world),
                            Vector3.Transform(evaluatedMesh.Vertices[face.B].Position, world),
                            Vector3.Transform(evaluatedMesh.Vertices[face.C].Position, world)));
                    }
                }

                return FaceSnapping.FindNearestFaceHit(ray, triangles);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Align to Surface: overwrites <paramref name="node"/>'s own rotation
        /// so its local +Z axis points exactly along <paramref name="worldFaceNormal"/> -
        /// the task's own "use vector math (Cross/Dot products) to calculate a rotation
        /// quaternion that aligns the dragged object's local Z-axis (Up vector) directly
        /// with the target face's normal vector" ask, via the same shortest-arc
        /// derivation (<see cref="RotationMath.ShortestArcRotation"/>, itself Cross/Dot
        /// under the hood) <see cref="Constraints.TrackToConstraint"/> already uses for
        /// its own "aim this fixed local axis at a world-space direction" problem - the
        /// exact same shape, just Z-to-normal here instead of TrackToConstraint's own
        /// configurable ForwardAxis-to-target-direction. A FRESH rotation computed from
        /// Vector3.UnitZ every call, not incrementally composed onto whatever rotation
        /// <paramref name="node"/> already had - the object's own roll AROUND that now-
        /// aligned normal is left unconstrained/arbitrary (the exact same disclosed
        /// "this fixes the aim axis only, not roll" limitation <see cref="Constraints.TrackToConstraint"/>'s
        /// own remarks already describe - there's no second reference axis given here
        /// either to pin roll down with).</summary>
        private static void ApplyAlignToSurface(CoreNode node, Vector3 worldFaceNormal)
        {
            var worldRotation = RotationMath.ShortestArcRotation(Vector3.UnitZ, worldFaceNormal);

            node.LocalRotation = node.Parent is null
                ? worldRotation
                : Quaternion.Normalize(Quaternion.Inverse(node.Parent.GetWorldRotation()) * worldRotation);
        }

        private static Vector3 ToVector3(Point3D p) => new((float)p.X, (float)p.Y, (float)p.Z);
        private static Vector3 ToVector3(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

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

            var previousLocalTransform = AffectOnlyOrigin ? node.GetLocalTransform() : default;

            var deltaRotation = Quaternion.CreateFromAxisAngle(worldAxis, (float)delta);
            var parentWorldRotation = node.Parent?.GetWorldRotation() ?? Quaternion.Identity;

            // worldRotation = parentWorldRotation * LocalRotation (see Node.GetWorldRotation).
            // newWorldRotation = deltaRotation * worldRotation (apply the existing
            // rotation first, then the world-axis delta on top of it - Quaternion
            // multiplication applies its right-hand operand first).
            // newLocalRotation = inverse(parentWorldRotation) * newWorldRotation.
            var newLocalRotation = Quaternion.Inverse(parentWorldRotation) * deltaRotation * parentWorldRotation * node.LocalRotation;
            node.LocalRotation = Quaternion.Normalize(newLocalRotation);

            if (AffectOnlyOrigin) node.CompensateMeshForOriginChange(previousLocalTransform);
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

            var previousLocalTransform = AffectOnlyOrigin ? node.GetLocalTransform() : default;

            const float minimumScale = 0.01f;
            var change = worldAxis * (float)delta;
            var newScale = node.LocalScale + change;

            node.LocalScale = new Vector3(
                MathF.Max(minimumScale, newScale.X),
                MathF.Max(minimumScale, newScale.Y),
                MathF.Max(minimumScale, newScale.Z));

            if (AffectOnlyOrigin) node.CompensateMeshForOriginChange(previousLocalTransform);
            Refresh();
        }
    }
}
