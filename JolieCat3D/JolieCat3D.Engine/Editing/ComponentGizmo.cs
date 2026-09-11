using System.ComponentModel;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
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

        private readonly HelixViewport3D _viewport;
        private readonly List<(Manipulator Manipulator, EventHandler ValueChangedHandler)> _activeManipulators = new();

        /// <summary>The session this gizmo currently drags - null when Edit Mode isn't
        /// active, or nothing in it is selected, in which case no handles are shown.</summary>
        public MeshEditSession? Session { get; private set; }

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
                manipulator.Position = position;
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
                descriptor?.RemoveValueChanged(manipulator, handler);
                _viewport.Children.Remove(manipulator);
            }
            _activeManipulators.Clear();

            if (Session?.GetSelectionWorldCentroid() is not { } centroid) return;
            var position = new Point3D(centroid.X, centroid.Y, centroid.Z);

            AddTranslateHandle(position, Vector3.UnitX, new Vector3D(1, 0, 0), AxisXColor);
            AddTranslateHandle(position, Vector3.UnitY, new Vector3D(0, 1, 0), AxisYColor);
            AddTranslateHandle(position, Vector3.UnitZ, new Vector3D(0, 0, 1), AxisZColor);
        }

        private void AddTranslateHandle(Point3D position, Vector3 worldAxis, Vector3D direction, Color color)
        {
            var manipulator = new TranslateManipulator
            {
                Position = position,
                Direction = direction,
                Diameter = 0.1,
                Length = 0.9,
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
        /// through <see cref="MeshEditSession.ApplyTranslation"/>.</summary>
        private void ApplyTranslate(Vector3 worldAxis, double delta)
        {
            if (Session?.Target is not { } node) return;

            var worldDelta = worldAxis * (float)delta;

            var world = node.GetWorldTransform();
            Matrix4x4.Decompose(world, out var scale, out var rotation, out _);
            var linear = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation);
            Matrix4x4.Invert(linear, out var invLinear);
            var localDelta = Vector3.TransformNormal(worldDelta, invLinear);

            Session.ApplyTranslation(localDelta);
            Refresh();
            EditApplied?.Invoke(this, EventArgs.Empty);
        }
    }
}
