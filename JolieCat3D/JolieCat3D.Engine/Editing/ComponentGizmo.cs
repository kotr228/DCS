using System.ComponentModel;
using System.Numerics;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Engine.Gizmos;
using JolieCat3D.Engine.Rendering;
using CoreNode = JolieCat3D.Core.Scene.Node;

namespace JolieCat3D.Engine.Editing
{
    /// <summary>One whole vertex-drag gesture's own before/after positions, raised by
    /// <see cref="ComponentGizmo.TranslationCommitted"/> - the Undo/Redo system's own
    /// entry point (<c>Service.Commands.VertexTranslateCommand</c>) for turning a
    /// completed component drag into one recorded command, mirroring
    /// <c>Gizmos.TransformCommittedEventArgs</c>'s own role for whole-node drags.
    /// Carries only the vertices that actually moved (see
    /// <see cref="ComponentGizmo.TrackDelta"/>'s own before/after comparison), not
    /// necessarily every vertex that was selected.</summary>
    public sealed class VertexTranslationCommittedEventArgs : EventArgs
    {
        public CoreNode Target { get; }
        public IReadOnlyList<(int Index, Vector3 Before, Vector3 After)> Changes { get; }

        public VertexTranslationCommittedEventArgs(CoreNode target, IReadOnlyList<(int Index, Vector3 Before, Vector3 After)> changes)
        {
            Target = target;
            Changes = changes;
        }
    }

    /// <summary>
    /// Edit Mode's own translate-only gizmo: a single set of Translate handles
    /// (mirroring <c>Gizmos.TransformGizmo</c>'s own Translate mode exactly - same
    /// manipulator type, same delta-tracking approach, same Rebuild-vs-Refresh split)
    /// positioned at the current <see cref="MeshEditSession"/>'s selection centroid,
    /// dragging every selected vertex together via
    /// <see cref="MeshEditSession.ApplyTranslation"/> instead of a whole node's
    /// <see cref="Core.Scene.Node.LocalPosition"/>. Rotate/Scale aren't offered for a
    /// component selection - rotating or scaling a handful of vertices isn't a
    /// well-defined single operation without also picking a pivot convention, a
    /// deliberate first-pass scope cut, the same kind <c>Gizmos.TransformGizmo</c>'s own
    /// remarks disclose for reusing <c>TranslateManipulator</c> as Scale's own visual.
    /// </summary>
    public sealed class ComponentGizmo
    {
        private static readonly Color AxisXColor = Color.FromRgb(0xE0, 0x50, 0x50);
        private static readonly Color AxisYColor = Color.FromRgb(0x50, 0xC0, 0x50);
        private static readonly Color AxisZColor = Color.FromRgb(0x40, 0x80, 0xE0);

        /// <summary>See <c>Gizmos.TransformGizmo</c>'s own matching factor constants and
        /// remarks - REACH (<see cref="TranslateManipulator.Length"/>) scales with the
        /// edited mesh's own current world-space size (<see cref="GetTargetMaxExtent"/>),
        /// while THICKNESS (<see cref="TranslateManipulator.Diameter"/>) stays a small,
        /// near-constant fraction of that same reach - a fixed world-unit size (this class
        /// used Diameter/Length pairs down to 0.015/0.4 before) either swallowed a small
        /// mesh or rendered too thin to see on a large one.</summary>
        private const double TranslateLengthFactor = 1.5;
        private const double TranslateDiameterFactor = 0.02;

        /// <summary>See <c>Gizmos.TransformGizmo.MinDiameter</c>'s own remarks - the floor
        /// under a computed Diameter so an extremely small (or mesh-less) selection can't
        /// compute a thickness that renders as nothing.</summary>
        private const double MinDiameter = 0.01;

        /// <summary>See <c>Gizmos.TransformGizmo.DefaultExtent</c>'s own remarks - the
        /// fallback size when <see cref="GetTargetMaxExtent"/> has no mesh bounds to work
        /// from at all.</summary>
        private const double DefaultExtent = 1.0;

        /// <summary>A faint, translucent gold - reads as "a reference indicator", never
        /// competes with the mesh/selection markers it's drawn alongside - for
        /// <see cref="_proportionalRadiusVisual"/>. Frozen once, the same
        /// <c>Rendering.Scene3DRenderer.FreezeBrush</c> reasoning: a brush only ever used
        /// read-only, safe to share and cheaper to hand to WPF frozen than not.</summary>
        private static readonly Brush ProportionalRadiusBrush = FreezeBrush(new SolidColorBrush(Color.FromArgb(0x30, 0xC2, 0x9B, 0x58)));

        private static Brush FreezeBrush(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        private readonly HelixViewport3D _viewport;
        private readonly List<(Manipulator Manipulator, EventHandler ValueChangedHandler)> _activeManipulators = new();
        private Visual3D? _proportionalRadiusVisual;

        /// <summary>The manipulators currently shown in the viewport (empty when Edit Mode
        /// isn't active or nothing is selected) - exposed so
        /// <see cref="JolieCat3D.Engine.Gizmos.GizmoHitTester"/> can hit-test a viewport
        /// click against them directly, independent of WPF's own 3D hit-testing (see that
        /// class's own remarks for why that's necessary).</summary>
        public IReadOnlyList<Manipulator> Handles => _activeManipulators.Select(t => t.Manipulator).ToList();

        /// <summary>The session this gizmo currently drags - null when Edit Mode isn't
        /// active, or nothing in it is selected, in which case no handles are shown.</summary>
        public MeshEditSession? Session { get; private set; }

        private TransformSpace _space = TransformSpace.Global;

        /// <summary>Which axes the vertex-drag handles show/drag along - see
        /// <see cref="Gizmos.TransformSpace"/>'s own remarks (mirrors
        /// <c>Gizmos.TransformGizmo.Space</c> exactly, just for this class's own
        /// Translate-only handle set). Changing this rebuilds the handles at their new
        /// orientation immediately.</summary>
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

        /// <summary>The LOCAL-space grid increment a vertex drag snaps to while Ctrl is
        /// held - see <c>Gizmos.TransformGizmo.GridSize</c>'s own remarks; no effect
        /// unless Ctrl is actually down during the drag. 1 by default.</summary>
        public float GridSize { get; set; } = 1f;

        /// <summary>This drag gesture's own running total (unsnapped) LOCAL delta since
        /// it began, and the portion of it already applied to the selection so far - see
        /// <see cref="ApplyTranslate"/>'s own remarks on why an incremental operation
        /// (<see cref="MeshEditSession.ApplyTranslation"/> moves every selected vertex by
        /// whatever delta it's given, not to an absolute position) needs to track both,
        /// not just the raw per-tick delta, to grid-snap correctly without drifting.</summary>
        private Vector3 _accumulatedRawDelta;
        private Vector3 _previouslyAppliedDelta;

        /// <summary>Raised after any drag actually moves the selection - the caller's
        /// cue to re-render the scene (which also refreshes the marker overlay - see
        /// <c>Rendering.Scene3DRenderer.Render</c>/<c>Refresh</c>) so both catch up to
        /// the new vertex positions. Fires on every intermediate tick of an in-progress
        /// drag, for live visual feedback - see <see cref="TranslationCommitted"/> for
        /// the "the WHOLE drag gesture just finished" signal instead.</summary>
        public event EventHandler? EditApplied;

        /// <summary>Raised exactly once when a WHOLE vertex-drag gesture ends (the
        /// manipulator releases mouse capture - see <see cref="TrackDelta"/>), carrying
        /// the before/after position of every vertex the gesture actually moved -
        /// <c>JolieCat3D.UI</c>'s own cue to record ONE
        /// <c>Service.Commands.VertexTranslateCommand</c> for the whole drag, not one per
        /// <see cref="EditApplied"/> tick, mirroring
        /// <c>Gizmos.TransformGizmo.TransformCommitted</c>'s exact role for whole-node
        /// drags. Not raised at all if the drag ended exactly where it started, or if
        /// nothing was selected when the drag began.</summary>
        public event EventHandler<VertexTranslationCommittedEventArgs>? TranslationCommitted;

        public ComponentGizmo(HelixViewport3D viewport) =>
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

        /// <summary>Attaches to <paramref name="session"/> (or detaches, for null) and
        /// rebuilds handles at its current selection centroid - call after any
        /// selection change (a new vertex/edge/face picked, or Edit Mode entered/exited),
        /// never from inside an active drag (see <see cref="Refresh"/>'s own remarks).</summary>
        public void Attach(MeshEditSession? session)
        {
            Session = session;
            Rebuild();
        }

        /// <summary>Repositions the existing handles at the selection's current centroid
        /// without tearing them down - safe to call mid-drag (from inside
        /// <see cref="TrackDelta"/>'s own callback), unlike <see cref="Rebuild"/>, which
        /// would drop the manipulator's own mouse capture mid-gesture.</summary>
        public void Refresh()
        {
            if (Session?.GetSelectionWorldCentroid() is not { } centroid) return;
            var position = new Point3D(centroid.X, centroid.Y, centroid.Z);

            foreach (var (manipulator, _) in _activeManipulators)
            {
                // A manipulator can be asked to refresh a moment after it's actually been
                // detached from the viewport (see Rebuild()'s own remarks) - HelixToolkit.Wpf's
                // own internal handling of a Position change on a detached manipulator
                // resolves its own Viewport3D and THROWS rather than returning null once
                // it isn't attached (see Rendering.Visual3DExtensions.GetViewport3DOrNull's
                // own remarks) - skip a detached manipulator entirely rather than crash.
                if (manipulator.GetViewport3DOrNull() is null) continue;
                manipulator.Position = position;
            }

            UpdateHandleSizing();
        }

        /// <summary>Tears down and recreates every handle at the selection's current
        /// centroid - a no-op result (no handles at all) if <see cref="Session"/> is
        /// null or has nothing selected. Call after a selection change; never from
        /// inside an active drag.</summary>
        public void Rebuild()
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(Manipulator.ValueProperty, typeof(Manipulator));
            foreach (var (manipulator, handler) in _activeManipulators)
            {
                // A manipulator being torn down can still hold an ACTIVE mouse capture -
                // this Rebuild() call itself is reachable reentrantly mid-drag (see
                // Gizmos.TransformGizmo.Rebuild's own remarks on the exact scenario this
                // guards against - the same reasoning applies here). Releasing capture
                // FIRST, before removing the visual, is what stops WPF from delivering any
                // further mouse event to a manipulator HelixToolkit.Wpf's own code would
                // otherwise crash trying to resolve the (by-then-detached) Viewport3D of.
                if (manipulator.IsMouseCaptured) manipulator.ReleaseMouseCapture();

                descriptor?.RemoveValueChanged(manipulator, handler);
                _viewport.Children.Remove(manipulator);
            }
            _activeManipulators.Clear();

            if (Session?.GetSelectionWorldCentroid() is not { } centroid) return;
            var position = new Point3D(centroid.X, centroid.Y, centroid.Z);
            var (xAxis, yAxis, zAxis) = GetActiveAxes();

            AddTranslateHandle(position, xAxis, ToDirection(xAxis), AxisXColor);
            AddTranslateHandle(position, yAxis, ToDirection(yAxis), AxisYColor);
            AddTranslateHandle(position, zAxis, ToDirection(zAxis), AxisZColor);

            UpdateHandleSizing();
        }

        /// <summary>See <c>Gizmos.TransformGizmo.GetActiveAxes</c>/<c>GetLocalAxes</c>'s own
        /// matching remarks - plain world unit axes for <see cref="Gizmos.TransformSpace.Global"/>,
        /// or <see cref="MeshEditSession.Target"/>'s own current world-rotated local axes
        /// for <see cref="Gizmos.TransformSpace.Local"/>. <see cref="ApplyTranslate"/> needs
        /// no change either way for the same reason that method's own remarks already give:
        /// it already treats whatever <c>worldAxis</c> it's handed as just "a world-space
        /// direction", with no assumption baked in about which one.</summary>
        private (Vector3 X, Vector3 Y, Vector3 Z) GetActiveAxes()
        {
            if (Space == TransformSpace.Global || Session?.Target is not { } node)
                return (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

            var rotation = node.GetWorldRotation();
            return (
                Vector3.Transform(Vector3.UnitX, rotation),
                Vector3.Transform(Vector3.UnitY, rotation),
                Vector3.Transform(Vector3.UnitZ, rotation));
        }

        private static Vector3D ToDirection(Vector3 axis) => new(axis.X, axis.Y, axis.Z);

        /// <summary>Draws a faint, translucent sphere around the selection's own
        /// pre-drag world centroid, radius <see cref="MeshEditSession.ProportionalRadius"/> -
        /// the visual "how far does this drag actually reach" cue Proportional Editing's
        /// own toolbar toggle asks for, shown only while BOTH a drag is actually in
        /// progress AND <see cref="MeshEditSession.ProportionalEditingEnabled"/> is on (a
        /// permanently-visible sphere around every selection, drag or not, would be a
        /// constant, unrequested visual distraction the rest of the time). Called from
        /// <see cref="TrackDelta"/>'s own GotMouseCapture handler, AFTER
        /// <see cref="MeshEditSession.BeginProportionalDrag"/> has already run, so the
        /// sphere's center reflects the selection's centroid at the exact same moment the
        /// falloff weights themselves were computed from - fixed there for the whole
        /// gesture, not chasing the selection as it moves mid-drag (which would misrepresent
        /// the radius the ALREADY-COMPUTED falloff actually used).</summary>
        private void ShowProportionalRadiusVisual()
        {
            HideProportionalRadiusVisual();

            if (Session is not { ProportionalEditingEnabled: true } session) return;
            if (session.GetSelectionWorldCentroid() is not { } centroid) return;

            var sphere = new SphereVisual3D
            {
                Center = new Point3D(centroid.X, centroid.Y, centroid.Z),
                Radius = session.ProportionalRadius,
                ThetaDiv = 24,
                PhiDiv = 12,
                Fill = ProportionalRadiusBrush,
            };

            _proportionalRadiusVisual = sphere;
            _viewport.Children.Add(sphere);
        }

        private void HideProportionalRadiusVisual()
        {
            if (_proportionalRadiusVisual is null) return;
            _viewport.Children.Remove(_proportionalRadiusVisual);
            _proportionalRadiusVisual = null;
        }

        /// <summary>Resizes every currently-active handle to match the edited mesh's own
        /// CURRENT world-space size, mirroring <c>Gizmos.TransformGizmo.UpdateHandleSizing</c>'s
        /// own reasoning exactly (private there, so not directly linkable here) - reach
        /// scales with the object, thickness stays a small, near-constant fraction of that
        /// reach. Sized off <see cref="MeshEditSession.Target"/>'s own WHOLE-mesh bounds
        /// (<see cref="GetTargetMaxExtent"/>), not the selected vertices' own (often much
        /// smaller) local extent - a single-vertex selection on a large mesh should still
        /// get a handle sized to the mesh it belongs to, not shrink to match one point.
        /// Called after <see cref="Rebuild"/> and after every <see cref="Refresh"/> (a drag
        /// tick, a Properties Inspector edit) since either can change the target's own Scale
        /// and therefore its bounds.</summary>
        private void UpdateHandleSizing()
        {
            if (Session?.Target is not { } node) return;

            var maxExtent = GetTargetMaxExtent(node);

            foreach (var (manipulator, _) in _activeManipulators)
            {
                if (manipulator is not TranslateManipulator translate) continue;
                if (translate.GetViewport3DOrNull() is null) continue;

                translate.Length = maxExtent * TranslateLengthFactor;
                translate.Diameter = Math.Max(MinDiameter, maxExtent * TranslateDiameterFactor);
            }
        }

        /// <summary>See <c>Gizmos.TransformGizmo.GetTargetMaxExtent</c>'s own remarks - the
        /// largest of <paramref name="node"/>'s own world-space bounding-box dimensions, or
        /// <see cref="DefaultExtent"/> if its mesh is missing/empty (<see cref="CoreNode.GetWorldBounds"/>'s
        /// own documented collapse-to-a-point behavior for that case).</summary>
        private static double GetTargetMaxExtent(CoreNode node)
        {
            var (min, max) = node.GetWorldBounds();
            var size = max - min;
            var maxExtent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
            return maxExtent > 1e-4f ? maxExtent : DefaultExtent;
        }

        private void AddTranslateHandle(Point3D position, Vector3 worldAxis, Vector3D direction, Color color)
        {
            // Diameter/Length are placeholders, immediately overwritten by
            // UpdateHandleSizing (called at the end of the very same Rebuild() this
            // method's own caller is inside) once the target's actual bounds are known.
            var manipulator = new TranslateManipulator
            {
                Position = position,
                Direction = direction,
                Diameter = MinDiameter,
                Length = DefaultExtent,
                Color = color,
            };

            TrackDelta(manipulator, delta => ApplyTranslate(worldAxis, delta));
        }

        /// <summary>Same delta-tracking approach as <c>Gizmos.TransformGizmo.TrackDelta</c>
        /// - see its own remarks for why (newValue - lastValue) is correct regardless of
        /// whether <see cref="Manipulator.Value"/> itself resets each drag or runs
        /// cumulatively. Also mirrors that method's own GotMouseCapture/LostMouseCapture
        /// bracketing of one whole drag gesture, here snapshotting every currently
        /// selected vertex's own position at drag-start and comparing it against the
        /// same vertices' positions at drag-end to raise <see cref="TranslationCommitted"/>
        /// with only the ones that actually moved.</summary>
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
            };

            var descriptor = DependencyPropertyDescriptor.FromProperty(Manipulator.ValueProperty, typeof(Manipulator));
            descriptor?.AddValueChanged(manipulator, handler);

            // Plain CLR event subscriptions, unlike the DependencyPropertyDescriptor one
            // above - no explicit unsubscription needed in Rebuild(): once a discarded
            // manipulator has no other reference keeping it alive, it (and these two
            // handlers along with it) become garbage-collectible together regardless -
            // the same reasoning TransformGizmo.TrackDelta's own remarks disclose.
            List<(int Index, Vector3 Position)>? dragStartPositions = null;

            manipulator.GotMouseCapture += (_, _) =>
            {
                // BeginProportionalDrag FIRST - it's what AffectedVertexIndices (used
                // immediately below) actually reflects for the rest of this drag; see its
                // own remarks on why this has to happen once, right at drag-start, rather
                // than being recomputed on the fly.
                Session?.BeginProportionalDrag();
                ShowProportionalRadiusVisual();

                if (Session?.Target?.Mesh is { } mesh && Session.AffectedVertexIndices.Count > 0)
                    dragStartPositions = Session.AffectedVertexIndices
                        .Select(index => (index, mesh.Vertices[index].Position))
                        .ToList();

                _accumulatedRawDelta = Vector3.Zero;
                _previouslyAppliedDelta = Vector3.Zero;
            };

            manipulator.LostMouseCapture += (_, _) =>
            {
                if (Session?.Target is { } node && node.Mesh is { } mesh && dragStartPositions is { } before)
                {
                    var changes = before
                        .Select(entry => (entry.Index, Before: entry.Position, After: mesh.Vertices[entry.Index].Position))
                        .Where(change => change.Before != change.After)
                        .ToList();

                    if (changes.Count > 0)
                        TranslationCommitted?.Invoke(this, new VertexTranslationCommittedEventArgs(node, changes));
                }

                dragStartPositions = null;
                _accumulatedRawDelta = Vector3.Zero;
                _previouslyAppliedDelta = Vector3.Zero;
                Session?.EndProportionalDrag();
                HideProportionalRadiusVisual();
            };

            _viewport.Children.Add(manipulator);
            _activeManipulators.Add((manipulator, handler));
        }

        /// <summary>Converts a world-space delta along <paramref name="worldAxis"/> into
        /// the matching change in the target mesh's own LOCAL vertex space - unlike
        /// <c>Gizmos.TransformGizmo.ApplyTranslate</c> (which only ever needs to unwind
        /// its target's PARENT's world rotation/scale, since a node's own
        /// <see cref="Core.Scene.Node.LocalPosition"/> lives in the parent's frame), a
        /// mesh's own vertex positions live in the node's OWN local space - so this
        /// unwinds the whole target node's <see cref="Core.Scene.Node.GetWorldTransform"/>
        /// linear (scale+rotation) part instead, via the same
        /// <see cref="Matrix4x4.Decompose"/>-then-invert approach, before applying it
        /// through <see cref="MeshEditSession.ApplyTranslation"/>.
        ///
        /// While Ctrl is held (checked fresh every tick, so toggling it mid-drag takes
        /// effect immediately), the selection snaps to <see cref="GridSize"/> increments -
        /// see <see cref="_accumulatedRawDelta"/>'s own remarks. Since
        /// <see cref="MeshEditSession.ApplyTranslation"/> takes an INCREMENTAL delta (it
        /// moves every selected vertex by whatever it's given, it has no absolute
        /// "position" of its own to overwrite the way <c>Gizmos.TransformGizmo.ApplyTranslate</c>
        /// can just set <c>LocalPosition</c> to an absolute value), the snapped TOTAL is
        /// diffed against whatever total was already applied on a previous tick, and only
        /// that difference is actually passed to <see cref="MeshEditSession.ApplyTranslation"/> -
        /// applying the full snapped total again every tick would move the selection by
        /// that amount ON TOP OF what a previous tick already moved it, compounding far
        /// past the intended offset.</summary>
        private void ApplyTranslate(Vector3 worldAxis, double delta)
        {
            if (Session?.Target is not { } node) return;

            var worldDelta = worldAxis * (float)delta;

            var world = node.GetWorldTransform();
            Matrix4x4.Decompose(world, out var scale, out var rotation, out _);
            var linear = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation);
            Matrix4x4.Invert(linear, out var invLinear);
            var localDelta = Vector3.TransformNormal(worldDelta, invLinear);

            _accumulatedRawDelta += localDelta;
            var snapRequested = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var (incrementToApply, newAppliedTotal) = GridSnapping.ComputeSnappedIncrement(_accumulatedRawDelta, _previouslyAppliedDelta, snapRequested, GridSize);
            _previouslyAppliedDelta = newAppliedTotal;

            Session.ApplyTranslation(incrementToApply);
            Refresh();
            EditApplied?.Invoke(this, EventArgs.Empty);
        }
    }
}
