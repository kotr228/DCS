using System.ComponentModel;
using System.Numerics;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Numerics;
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

        /// <summary>Nominal <see cref="TranslateManipulator.Diameter"/>/<see cref="TranslateManipulator.Length"/>
        /// at <see cref="ReferenceDistance"/> world units from the camera - see
        /// <c>Gizmos.TransformGizmo</c>'s own matching constants/remarks for why these are
        /// deliberately much slimmer/shorter than this class used before (Diameter 0.1,
        /// Length 0.9, then 0.035/0.6 - still oversized in practice), and
        /// <see cref="RescaleHandles"/> for how they're scaled for the camera's current
        /// distance/zoom.</summary>
        private const double TranslateDiameter = 0.015;
        private const double TranslateLength = 0.4;
        private const double ReferenceDistance = 10.0;
        private const double MinScale = 0.15;

        /// <summary>See <c>Gizmos.TransformGizmo.MaxScale</c>'s own remarks - kept tight so a
        /// zoomed-out view can't balloon a handle back to an oversized one.</summary>
        private const double MaxScale = 3.0;

        private readonly HelixViewport3D _viewport;
        private readonly List<(Manipulator Manipulator, EventHandler ValueChangedHandler)> _activeManipulators = new();

        /// <summary>The manipulators currently shown in the viewport (empty when Edit Mode
        /// isn't active or nothing is selected) - exposed so
        /// <see cref="JolieCat3D.Engine.Gizmos.GizmoHitTester"/> can hit-test a viewport
        /// click against them directly, independent of WPF's own 3D hit-testing (see that
        /// class's own remarks for why that's necessary).</summary>
        public IReadOnlyList<Manipulator> Handles => _activeManipulators.Select(t => t.Manipulator).ToList();

        /// <summary>The session this gizmo currently drags - null when Edit Mode isn't
        /// active, or nothing in it is selected, in which case no handles are shown.</summary>
        public MeshEditSession? Session { get; private set; }

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

        public ComponentGizmo(HelixViewport3D viewport)
        {
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

            // See Gizmos.TransformGizmo's own matching subscription/remarks - zooming the
            // camera alone never calls Rebuild/Refresh on its own.
            _viewport.CameraChanged += (_, _) => RescaleHandles();
        }

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

            RescaleHandles();
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

            AddTranslateHandle(position, Vector3.UnitX, new Vector3D(1, 0, 0), AxisXColor);
            AddTranslateHandle(position, Vector3.UnitY, new Vector3D(0, 1, 0), AxisYColor);
            AddTranslateHandle(position, Vector3.UnitZ, new Vector3D(0, 0, 1), AxisZColor);

            RescaleHandles();
        }

        /// <summary>Rescales every currently-active handle so its on-screen size stays
        /// roughly constant as the camera zooms in/out - see
        /// <see cref="JolieCat3D.Engine.Gizmos.TransformGizmo"/>'s own matching method
        /// (private, so not directly linkable here) for why (HelixToolkit.Wpf 2.24.0 has
        /// no built-in option for this) and how (perspective vs. orthographic camera
        /// handled separately). This class's handles are always Translate handles (no
        /// Rotate/Scale mode of its own - see this class's own remarks), so unlike that
        /// method, there's no mode check needed here.</summary>
        private void RescaleHandles()
        {
            if (Session?.GetSelectionWorldCentroid() is not { } centroid) return;

            var scale = _viewport.Camera switch
            {
                PerspectiveCamera perspective => ComputeDistanceScale(centroid, perspective.Position),
                OrthographicCamera orthographic => Math.Clamp(orthographic.Width / ReferenceDistance, MinScale, MaxScale),
                _ => (double?)null,
            };
            if (scale is not { } s) return;

            foreach (var (manipulator, _) in _activeManipulators)
            {
                if (manipulator is not TranslateManipulator translate) continue;
                if (translate.GetViewport3DOrNull() is null) continue;

                translate.Diameter = TranslateDiameter * s;
                translate.Length = TranslateLength * s;
            }
        }

        private static double ComputeDistanceScale(Vector3 centroid, Point3D cameraPosition)
        {
            var position = new Point3D(centroid.X, centroid.Y, centroid.Z);
            var distance = (position - cameraPosition).Length;
            return Math.Clamp(distance / ReferenceDistance, MinScale, MaxScale);
        }

        private void AddTranslateHandle(Point3D position, Vector3 worldAxis, Vector3D direction, Color color)
        {
            var manipulator = new TranslateManipulator
            {
                Position = position,
                Direction = direction,
                Diameter = TranslateDiameter,
                Length = TranslateLength,
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
                if (Session?.Target?.Mesh is { } mesh && Session.SelectedVertexIndices.Count > 0)
                    dragStartPositions = Session.SelectedVertexIndices
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
