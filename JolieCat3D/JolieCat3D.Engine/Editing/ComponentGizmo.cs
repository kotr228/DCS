using System.Numerics;
using System.Windows;
using System.Windows.Input;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Engine.Gizmos;
using JolieCat3D.Engine.Viewport;
using CoreNode = JolieCat3D.Core.Scene.Node;

namespace JolieCat3D.Engine.Editing
{
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
    /// Edit-mode move gizmo for the selected vertices/edges/faces of a
    /// <see cref="MeshEditSession"/>. Same handle layout as the object gizmo's translate
    /// mode; Ctrl snaps to the grid, proportional editing falls off over
    /// <see cref="MeshEditSession.ProportionalRadius"/>.
    /// </summary>
    public sealed class ComponentGizmo
    {
        private static readonly Color4 ProportionalColor = new(0xC2 / 255f, 0x9B / 255f, 0x58 / 255f, 0.8f);

        private readonly IViewportProjector _projector;
        private GizmoPart _hover = GizmoPart.None;
        private GizmoPart _active = GizmoPart.None;

        private Vector3 _startCenter;
        private Vector3[] _startAxes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
        private float _startAxisParam;
        private Vector3 _startPlaneHit;
        private Vector3 _previouslyAppliedDelta;
        private List<(int Index, Vector3 Position)>? _dragStartPositions;

        public ComponentGizmo(IViewportProjector projector) =>
            _projector = projector ?? throw new ArgumentNullException(nameof(projector));

        public MeshEditSession? Session { get; private set; }

        public TransformSpace Space { get; set; } = TransformSpace.Global;

        public float GridSize { get; set; } = 1f;

        public bool IsDragging => _active != GizmoPart.None;

        public event EventHandler? EditApplied;
        public event EventHandler<VertexTranslationCommittedEventArgs>? TranslationCommitted;

        public void Attach(MeshEditSession? session)
        {
            if (IsDragging) CancelDrag();
            Session = session;
            _hover = GizmoPart.None;
        }

        public void Refresh() { }

        public void Rebuild() { }

        private Vector3? Center => Session?.GetSelectionWorldCentroid();

        private Vector3[] Axes()
        {
            if (Space == TransformSpace.Global || Session?.Target is not { } node)
                return new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            var r = node.GetWorldRotation();
            return new[]
            {
                Vector3.Normalize(Vector3.Transform(Vector3.UnitX, r)),
                Vector3.Normalize(Vector3.Transform(Vector3.UnitY, r)),
                Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, r)),
            };
        }

        public void Draw(OverlayDrawList list)
        {
            if (Center is not { } center) return;
            var axes = IsDragging ? _startAxes : Axes();
            GizmoHandles.DrawTranslate(list, _projector, center, axes, _hover, _active);

            if (IsDragging && Session is { ProportionalEditingEnabled: true } session)
            {
                var forward = Vector3.Normalize(_projector.ScreenToRay(_projector.WorldToScreen(center) ?? new Point()).Direction);
                list.AddCircle(center, forward, session.ProportionalRadius, ProportionalColor, 1.5f, OverlayLayer.OnTop, 64);
            }
        }

        public bool UpdateHover(Point mouse)
        {
            if (IsDragging) return false;
            var hit = HitTest(mouse);
            if (hit == _hover) return false;
            _hover = hit;
            return true;
        }

        public GizmoPart HitTest(Point mouse) =>
            Center is { } center ? GizmoHandles.HitTranslateOrScale(_projector, center, Axes(), mouse, planes: true) : GizmoPart.None;

        public bool TryBeginDrag(Point mouse)
        {
            if (Session is not { Target.Mesh: not null } || Center is null) return false;
            var part = HitTest(mouse);
            if (part == GizmoPart.None) return false;
            StartDrag(part, mouse);
            return true;
        }

        /// <summary>True during G (grab): the selection follows the mouse until confirmed/cancelled.</summary>
        public bool IsModal { get; private set; }

        public bool BeginModal(Point mouse)
        {
            if (Session is not { Target.Mesh: not null } || Center is null) return false;
            if (IsDragging) CancelDrag();
            IsModal = true;
            StartDrag(GizmoPart.View, mouse);
            return true;
        }

        public void ConstrainModal(int axis, Point mouse)
        {
            if (!IsModal || Session?.Target?.Mesh is not { } mesh || _dragStartPositions is not { } before) return;
            var part = _active == GizmoHandles.AxisPart(axis) ? GizmoPart.View : GizmoHandles.AxisPart(axis);
            foreach (var (index, position) in before)
                if (index < mesh.Vertices.Count) mesh.SetVertexPosition(index, position);
            Session.EndProportionalDrag();
            var start = _modalStartMouse;
            StartDrag(part, start);
            UpdateDrag(mouse);
        }

        private Point _modalStartMouse;

        private void StartDrag(GizmoPart part, Point mouse)
        {
            var session = Session!;
            var mesh = session.Target!.Mesh!;
            var center = Center!.Value;
            _modalStartMouse = mouse;
            _active = part;
            _startCenter = center;
            _startAxes = Axes();
            _previouslyAppliedDelta = Vector3.Zero;

            var ray = _projector.ScreenToRay(mouse);
            if (GizmoHandles.IsAxis(part))
                _startAxisParam = GizmoHandles.AxisParameter(ray, center, _startAxes[GizmoHandles.AxisIndex(part)]) ?? 0f;
            _startPlaneHit = GizmoHandles.IntersectPlane(ray, center, PlaneNormal(part)) ?? center;

            session.BeginProportionalDrag();
            _dragStartPositions = session.AffectedVertexIndices.Select(i => (i, mesh.Vertices[i].Position)).ToList();
        }

        private Vector3 PlaneNormal(GizmoPart part) =>
            GizmoHandles.IsPlane(part)
                ? _startAxes[GizmoHandles.AxisIndex(part)]
                : Vector3.Normalize(_projector.ScreenToRay(_projector.WorldToScreen(_startCenter) ?? new Point()).Direction);

        public void UpdateDrag(Point mouse)
        {
            if (Session?.Target is not { } node || !IsDragging) return;
            var ray = _projector.ScreenToRay(mouse);
            Vector3 worldDelta;
            if (GizmoHandles.IsAxis(_active))
            {
                var axis = _startAxes[GizmoHandles.AxisIndex(_active)];
                if (GizmoHandles.AxisParameter(ray, _startCenter, axis) is not { } t) return;
                worldDelta = axis * (t - _startAxisParam);
            }
            else
            {
                if (GizmoHandles.IntersectPlane(ray, _startCenter, PlaneNormal(_active)) is not { } hit) return;
                worldDelta = hit - _startPlaneHit;
            }

            // World delta -> mesh-local delta (undo the node's rotation and scale).
            Matrix4x4.Decompose(node.GetWorldTransform(), out var scale, out var rotation, out _);
            var linear = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation);
            var localDelta = Matrix4x4.Invert(linear, out var inv) ? Vector3.TransformNormal(worldDelta, inv) : worldDelta;

            var snap = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var (increment, newTotal) = GridSnapping.ComputeSnappedIncrement(localDelta, _previouslyAppliedDelta, snap, GridSize);
            _previouslyAppliedDelta = newTotal;
            if (increment == Vector3.Zero) return;
            Session.ApplyTranslation(increment);
            EditApplied?.Invoke(this, EventArgs.Empty);
        }

        public void EndDrag()
        {
            IsModal = false;
            if (!IsDragging) return;
            _active = GizmoPart.None;
            if (Session?.Target is { Mesh: { } mesh } node && _dragStartPositions is { } before)
            {
                var changes = before
                    .Select(entry => (entry.Index, Before: entry.Position, After: mesh.Vertices[entry.Index].Position))
                    .Where(change => change.Before != change.After)
                    .ToList();
                if (changes.Count > 0) TranslationCommitted?.Invoke(this, new VertexTranslationCommittedEventArgs(node, changes));
            }
            _dragStartPositions = null;
            Session?.EndProportionalDrag();
        }

        public void CancelDrag()
        {
            IsModal = false;
            if (IsDragging && Session?.Target?.Mesh is { } mesh && _dragStartPositions is { } before)
            {
                foreach (var (index, position) in before)
                    if (index < mesh.Vertices.Count) mesh.SetVertexPosition(index, position);
                EditApplied?.Invoke(this, EventArgs.Empty);
            }
            _active = GizmoPart.None;
            _dragStartPositions = null;
            Session?.EndProportionalDrag();
        }
    }
}
