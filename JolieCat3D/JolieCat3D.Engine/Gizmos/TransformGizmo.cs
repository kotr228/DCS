using System.Numerics;
using System.Windows;
using System.Windows.Input;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Engine.Viewport;
using CoreNode = JolieCat3D.Core.Scene.Node;
using Quaternion = System.Numerics.Quaternion;

namespace JolieCat3D.Engine.Gizmos
{
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

    public sealed class MultiTransformCommittedEventArgs : EventArgs
    {
        public IReadOnlyList<(CoreNode Target, (Vector3 Position, Quaternion Rotation, Vector3 Scale) Before, (Vector3 Position, Quaternion Rotation, Vector3 Scale) After)> Changes { get; }

        public MultiTransformCommittedEventArgs(IReadOnlyList<(CoreNode, (Vector3, Quaternion, Vector3), (Vector3, Quaternion, Vector3))> changes) => Changes = changes;
    }

    /// <summary>
    /// Object translate/rotate/scale gizmo (Blender style). Renderer-agnostic: it draws into
    /// an <see cref="OverlayDrawList"/> and does all picking through an
    /// <see cref="IViewportProjector"/>. One drag gesture raises many
    /// <see cref="TransformChanged"/> and exactly one <see cref="TransformCommitted"/>.
    ///
    /// Modifiers while dragging: Ctrl snaps (grid / 15° / 0.1 scale steps), Shift snaps a
    /// translation to other objects' vertices/edges, falling back to their faces
    /// (optionally aligning to the surface).
    /// </summary>
    public sealed class TransformGizmo
    {
        private readonly IViewportProjector _projector;

        private GizmoPart _hover = GizmoPart.None;
        private GizmoPart _active = GizmoPart.None;

        // drag state
        private (Vector3 Position, Quaternion Rotation, Vector3 Scale) _start;
        private Vector3 _startCenter;
        private Vector3[] _startAxes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
        private float _startAxisParam;
        private Vector3 _startPlaneHit;
        private double _startAngle;
        private double _accumulatedAngle;
        private double _previousAngle;
        private Point _startMouse;

        public TransformGizmo(IViewportProjector projector) =>
            _projector = projector ?? throw new ArgumentNullException(nameof(projector));

        public GizmoMode Mode { get; set; } = GizmoMode.Translate;

        public TransformSpace Space { get; set; } = TransformSpace.Global;

        public CoreNode? Target { get; private set; }

        public float GridSize { get; set; } = 1f;

        /// <summary>Every node in the scene - used by Shift-snapping.</summary>
        public Func<IEnumerable<CoreNode>>? SceneNodes { get; set; }

        /// <summary>Move/rotate/scale only the origin; the mesh stays put in world space.</summary>
        public bool AffectOnlyOrigin { get; set; }

        /// <summary>When Shift-snapping to a face, also rotate +Z to the face normal.</summary>
        public bool AlignToSurfaceEnabled { get; set; }

        /// <summary>Hide the gizmo without detaching it (e.g. while navigating).</summary>
        public bool IsVisible { get; set; } = true;

        public bool IsDragging => _active != GizmoPart.None;

        public event EventHandler? TransformChanged;
        public event EventHandler<TransformCommittedEventArgs>? TransformCommitted;

        /// <summary>Raised instead of <see cref="TransformCommitted"/> when a whole group moved.</summary>
        public event EventHandler<MultiTransformCommittedEventArgs>? MultiTransformCommitted;

        /// <summary>Other selected objects that move together with <see cref="Target"/>.</summary>
        public Func<IReadOnlyList<CoreNode>>? AdditionalTargets { get; set; }

        private sealed record GroupMember(CoreNode Node, (Vector3 Position, Quaternion Rotation, Vector3 Scale) StartLocal, Vector3 StartWorldPosition, Quaternion StartWorldRotation);

        private readonly List<GroupMember> _group = new();
        private Vector3 _activeStartWorldPosition;
        private Quaternion _activeStartWorldRotation;

        /// <summary>Selected objects other than the target, minus any whose ancestor is
        /// also selected (those already follow their parent).</summary>
        private List<CoreNode> OtherTargets()
        {
            if (Target is null || AdditionalTargets?.Invoke() is not { Count: > 0 } extra) return new List<CoreNode>();
            var all = extra.Append(Target).ToHashSet();
            bool HasSelectedAncestor(CoreNode n)
            {
                for (var p = n.Parent; p is not null; p = p.Parent) if (all.Contains(p)) return true;
                return false;
            }
            return extra.Where(n => !ReferenceEquals(n, Target) && !HasSelectedAncestor(n)).Distinct().ToList();
        }

        private static void SetWorldPosition(CoreNode node, Vector3 world)
        {
            if (node.Parent is null) { node.LocalPosition = world; return; }
            if (Matrix4x4.Invert(node.Parent.GetWorldTransform(), out var inv)) node.LocalPosition = Vector3.Transform(world, inv);
        }

        private static void SetWorldRotation(CoreNode node, Quaternion world)
        {
            var parent = node.Parent?.GetWorldRotation() ?? Quaternion.Identity;
            node.LocalRotation = Quaternion.Normalize(Quaternion.Inverse(parent) * world);
        }

        /// <summary>Moves the rest of the group by the change the drag made to the target
        /// (rotation and scale pivot around the group median, Blender default).</summary>
        private void ApplyToGroup(CoreNode active)
        {
            if (_group.Count == 0) return;
            var pivot = _startCenter;
            switch (Mode)
            {
                case GizmoMode.Translate:
                {
                    var delta = active.GetWorldPosition() - _activeStartWorldPosition;
                    foreach (var m in _group) SetWorldPosition(m.Node, m.StartWorldPosition + delta);
                    break;
                }
                case GizmoMode.Rotate:
                {
                    var delta = Quaternion.Normalize(active.GetWorldRotation() * Quaternion.Inverse(_activeStartWorldRotation));
                    SetWorldPosition(active, pivot + Vector3.Transform(_activeStartWorldPosition - pivot, delta));
                    foreach (var m in _group)
                    {
                        SetWorldRotation(m.Node, delta * m.StartWorldRotation);
                        SetWorldPosition(m.Node, pivot + Vector3.Transform(m.StartWorldPosition - pivot, delta));
                    }
                    break;
                }
                case GizmoMode.Scale:
                {
                    var s0 = _start.Scale;
                    var s1 = active.LocalScale;
                    var ratio = new Vector3(Ratio(s1.X, s0.X), Ratio(s1.Y, s0.Y), Ratio(s1.Z, s0.Z));
                    Vector3 ScaleOffset(Vector3 offset) =>
                        _startAxes[0] * Vector3.Dot(offset, _startAxes[0]) * ratio.X +
                        _startAxes[1] * Vector3.Dot(offset, _startAxes[1]) * ratio.Y +
                        _startAxes[2] * Vector3.Dot(offset, _startAxes[2]) * ratio.Z;
                    SetWorldPosition(active, pivot + ScaleOffset(_activeStartWorldPosition - pivot));
                    foreach (var m in _group)
                    {
                        m.Node.LocalScale = m.StartLocal.Scale * ratio;
                        SetWorldPosition(m.Node, pivot + ScaleOffset(m.StartWorldPosition - pivot));
                    }
                    break;
                }
            }

            static float Ratio(float a, float b) => MathF.Abs(b) < 1e-8f ? 1f : a / b;
        }

        private void RestoreGroup()
        {
            foreach (var m in _group)
            {
                m.Node.LocalPosition = m.StartLocal.Position;
                m.Node.LocalRotation = m.StartLocal.Rotation;
                m.Node.LocalScale = m.StartLocal.Scale;
            }
        }

        public void Attach(CoreNode? node)
        {
            if (IsDragging) CancelDrag();
            Target = node;
            _hover = GizmoPart.None;
        }

        /// <summary>Kept for API compatibility - the gizmo reads the target every frame.</summary>
        public void Refresh() { }

        private Vector3 Center
        {
            get
            {
                if (Target is null) return Vector3.Zero;
                var others = OtherTargets();
                if (others.Count == 0) return Target.GetWorldPosition();
                var sum = Target.GetWorldPosition();
                foreach (var n in others) sum += n.GetWorldPosition();
                return sum / (others.Count + 1);
            }
        }

        private Vector3[] Axes()
        {
            if (Target is null || (Space == TransformSpace.Global && Mode != GizmoMode.Scale))
                return new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            var r = Target.GetWorldRotation();
            return new[]
            {
                Vector3.Normalize(Vector3.Transform(Vector3.UnitX, r)),
                Vector3.Normalize(Vector3.Transform(Vector3.UnitY, r)),
                Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, r)),
            };
        }

        private Vector3 CameraForward(Vector3 at) =>
            Vector3.Normalize(_projector.ScreenToRay(_projector.WorldToScreen(at) ?? new Point()).Direction);

        // ---------------- drawing ----------------

        public void Draw(OverlayDrawList list)
        {
            if (Target is null || !IsVisible) return;
            var center = IsDragging && Mode != GizmoMode.Translate ? _startCenter : Center;
            var axes = IsDragging ? _startAxes : Axes();
            switch (Mode)
            {
                case GizmoMode.Translate:
                    GizmoHandles.DrawTranslate(list, _projector, center, axes, _hover, _active);
                    if (IsDragging) DrawDragGuide(list, center, axes);
                    break;
                case GizmoMode.Rotate:
                    GizmoHandles.DrawRotate(list, _projector, center, axes, CameraForward(center), _hover, _active);
                    break;
                case GizmoMode.Scale:
                    GizmoHandles.DrawScale(list, _projector, center, axes, _hover, _active);
                    break;
            }
        }

        private void DrawDragGuide(OverlayDrawList list, Vector3 center, Vector3[] axes)
        {
            var i = GizmoHandles.AxisIndex(_active);
            if (!GizmoHandles.IsAxis(_active) || i < 0) return;
            var c = GizmoHandles.ColorOf(i);
            var far = _projector.WorldUnitsPerPixel(center) * 4000f;
            list.AddLine(_startCenter - axes[i] * far, _startCenter + axes[i] * far, new Color4(c.R, c.G, c.B, 0.6f), 1.2f, OverlayLayer.OnTop);
        }

        // ---------------- input ----------------

        /// <summary>Updates hover highlighting; returns true when the hover state changed.</summary>
        public bool UpdateHover(Point mouse)
        {
            if (IsDragging || Target is null || !IsVisible) return false;
            var hit = HitTest(mouse);
            if (hit == _hover) return false;
            _hover = hit;
            return true;
        }

        public GizmoPart HitTest(Point mouse)
        {
            if (Target is null || !IsVisible) return GizmoPart.None;
            var center = Center;
            var axes = Axes();
            return Mode == GizmoMode.Rotate
                ? GizmoHandles.HitRotate(_projector, center, axes, CameraForward(center), mouse)
                : GizmoHandles.HitTranslateOrScale(_projector, center, axes, mouse, planes: Mode == GizmoMode.Translate);
        }

        /// <summary>Starts dragging if <paramref name="mouse"/> is over a handle.</summary>
        public bool TryBeginDrag(Point mouse)
        {
            if (Target is null) return false;
            var part = HitTest(mouse);
            if (part == GizmoPart.None) return false;
            StartDrag(part, mouse);
            return true;
        }

        /// <summary>True during a Blender-style G/R/S operation: the transform follows the
        /// mouse without a button held until <see cref="EndDrag"/> / <see cref="CancelDrag"/>.</summary>
        public bool IsModal { get; private set; }

        private GizmoMode _modeBeforeModal;

        /// <summary>G / R / S: starts a free (view-plane / view-axis / uniform) transform.</summary>
        public bool BeginModal(GizmoMode mode, Point mouse)
        {
            if (Target is null) return false;
            if (IsDragging) CancelDrag();
            _modeBeforeModal = Mode;
            Mode = mode;
            IsModal = true;
            StartDrag(GizmoPart.View, mouse);
            return true;
        }

        /// <summary>X / Y / Z during a modal transform: constrain to that axis (press again to release).</summary>
        public void ConstrainModal(int axis, Point mouse)
        {
            if (!IsModal || Target is not { } node) return;
            var part = _active == GizmoHandles.AxisPart(axis) ? GizmoPart.View : GizmoHandles.AxisPart(axis);
            if (Mode == GizmoMode.Scale && part == GizmoPart.View) part = GizmoPart.View;
            var previousLocal = node.GetLocalTransform();
            node.LocalPosition = _start.Position;
            node.LocalRotation = _start.Rotation;
            node.LocalScale = _start.Scale;
            if (AffectOnlyOrigin && _group.Count == 0) node.CompensateMeshForOriginChange(previousLocal);
            RestoreGroup();
            StartDrag(part, _startMouse);
            UpdateDrag(mouse);
        }

        private void StartDrag(GizmoPart part, Point mouse)
        {
            var node = Target!;
            _active = part;
            _hover = part;
            _start = (node.LocalPosition, node.LocalRotation, node.LocalScale);
            _activeStartWorldPosition = node.GetWorldPosition();
            _activeStartWorldRotation = node.GetWorldRotation();
            _group.Clear();
            foreach (var other in OtherTargets())
                _group.Add(new GroupMember(other, (other.LocalPosition, other.LocalRotation, other.LocalScale), other.GetWorldPosition(), other.GetWorldRotation()));
            _startCenter = Center;
            _startAxes = Axes();
            _startMouse = mouse;

            var ray = _projector.ScreenToRay(mouse);
            if (GizmoHandles.IsAxis(part))
                _startAxisParam = GizmoHandles.AxisParameter(ray, _startCenter, _startAxes[GizmoHandles.AxisIndex(part)]) ?? 0f;
            _startPlaneHit = GizmoHandles.IntersectPlane(ray, _startCenter, DragPlaneNormal(part)) ?? _startCenter;
            _startAngle = _previousAngle = GizmoHandles.ScreenAngle(_projector, _startCenter, mouse);
            _accumulatedAngle = 0;
        }

        private Vector3 DragPlaneNormal(GizmoPart part)
        {
            if (GizmoHandles.IsPlane(part)) return _startAxes[GizmoHandles.AxisIndex(part)];
            return CameraForward(_startCenter);
        }

        public void UpdateDrag(Point mouse)
        {
            if (Target is not { } node || !IsDragging) return;
            var previousLocal = node.GetLocalTransform();
            var mods = Keyboard.Modifiers;

            switch (Mode)
            {
                case GizmoMode.Translate: DragTranslate(node, mouse, mods); break;
                case GizmoMode.Rotate: DragRotate(node, mouse, mods); break;
                case GizmoMode.Scale: DragScale(node, mouse, mods); break;
            }

            if (AffectOnlyOrigin && _group.Count == 0) node.CompensateMeshForOriginChange(previousLocal);
            ApplyToGroup(node);
            TransformChanged?.Invoke(this, EventArgs.Empty);
        }

        public void EndDrag()
        {
            LeaveModal();
            if (Target is not { } node || !IsDragging) { _active = GizmoPart.None; return; }
            _active = GizmoPart.None;
            var after = (node.LocalPosition, node.LocalRotation, node.LocalScale);
            var changed = _start.Position != after.LocalPosition || _start.Rotation != after.LocalRotation || _start.Scale != after.LocalScale;
            if (_group.Count > 0)
            {
                if (changed)
                {
                    var changes = new List<(CoreNode, (Vector3, Quaternion, Vector3), (Vector3, Quaternion, Vector3))> { (node, _start, after) };
                    foreach (var m in _group) changes.Add((m.Node, m.StartLocal, (m.Node.LocalPosition, m.Node.LocalRotation, m.Node.LocalScale)));
                    MultiTransformCommitted?.Invoke(this, new MultiTransformCommittedEventArgs(changes));
                }
                _group.Clear();
            }
            else if (changed)
                TransformCommitted?.Invoke(this, new TransformCommittedEventArgs(node, _start, after));
        }

        /// <summary>Esc / right click while dragging: restore the transform from before the drag.</summary>
        public void CancelDrag()
        {
            LeaveModal();
            if (Target is { } node && IsDragging)
            {
                var previousLocal = node.GetLocalTransform();
                node.LocalPosition = _start.Position;
                node.LocalRotation = _start.Rotation;
                node.LocalScale = _start.Scale;
                if (AffectOnlyOrigin && _group.Count == 0) node.CompensateMeshForOriginChange(previousLocal);
                RestoreGroup();
                _group.Clear();
                TransformChanged?.Invoke(this, EventArgs.Empty);
            }
            _active = GizmoPart.None;
        }

        private void LeaveModal()
        {
            if (!IsModal) return;
            IsModal = false;
            Mode = _modeBeforeModal;
        }

        // ---------------- translate ----------------

        private void DragTranslate(CoreNode node, Point mouse, ModifierKeys mods)
        {
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
                if (GizmoHandles.IntersectPlane(ray, _startCenter, DragPlaneNormal(_active)) is not { } hit) return;
                worldDelta = hit - _startPlaneHit;
            }

            var localDelta = WorldToParentDelta(node, worldDelta);

            if (mods.HasFlag(ModifierKeys.Shift) && TryFindVertexEdgeSnapPoint(ray) is { } snapPoint)
            {
                node.LocalPosition = ConvertWorldPointToLocal(node, snapPoint);
            }
            else if (mods.HasFlag(ModifierKeys.Shift) && TryFindFaceSnapHit(ray) is { } faceHit)
            {
                node.LocalPosition = ConvertWorldPointToLocal(node, faceHit.Point);
                if (AlignToSurfaceEnabled) ApplyAlignToSurface(node, faceHit.Normal);
            }
            else
            {
                node.LocalPosition = GridSnapping.ComputeSnappedPosition(_start.Position, localDelta, mods.HasFlag(ModifierKeys.Control), GridSize);
            }
        }

        private static Vector3 WorldToParentDelta(CoreNode node, Vector3 worldDelta)
        {
            if (node.Parent is null) return worldDelta;
            var parentWorld = node.Parent.GetWorldTransform();
            Matrix4x4.Decompose(parentWorld, out var parentScale, out var parentRotation, out _);
            var linear = Matrix4x4.CreateScale(parentScale) * Matrix4x4.CreateFromQuaternion(parentRotation);
            return Matrix4x4.Invert(linear, out var inv) ? Vector3.TransformNormal(worldDelta, inv) : worldDelta;
        }

        private static Vector3 ConvertWorldPointToLocal(CoreNode node, Vector3 worldPoint)
        {
            if (node.Parent is null) return worldPoint;
            if (!Matrix4x4.Invert(node.Parent.GetWorldTransform(), out var parentWorldToLocal)) return node.LocalPosition;
            return Vector3.Transform(worldPoint, parentWorldToLocal);
        }

        private List<(Vector3 A, Vector3 B, Vector3 C)> OtherTriangles()
        {
            var triangles = new List<(Vector3 A, Vector3 B, Vector3 C)>();
            if (SceneNodes is not { } provider || Target is not { } target) return triangles;
            var excluded = target.Traverse().ToHashSet();
            foreach (var candidate in provider())
            {
                if (excluded.Contains(candidate) || candidate.Mesh is not { } mesh) continue;
                Mesh evaluated;
                try { evaluated = ModifierStack.Evaluate(mesh, candidate.Modifiers, candidate); }
                catch (Exception) { continue; }
                var world = candidate.GetWorldTransform();
                foreach (var face in evaluated.GetRenderFaces())
                {
                    triangles.Add((
                        Vector3.Transform(evaluated.Vertices[face.A].Position, world),
                        Vector3.Transform(evaluated.Vertices[face.B].Position, world),
                        Vector3.Transform(evaluated.Vertices[face.C].Position, world)));
                }
            }
            return triangles;
        }

        private Vector3? TryFindVertexEdgeSnapPoint(Ray ray)
        {
            try { return VertexEdgeSnapping.FindNearestVertexOrEdge(ray, OtherTriangles()); }
            catch (Exception) { return null; }
        }

        private (Vector3 Point, Vector3 Normal)? TryFindFaceSnapHit(Ray ray)
        {
            try { return FaceSnapping.FindNearestFaceHit(ray, OtherTriangles()); }
            catch (Exception) { return null; }
        }

        private static void ApplyAlignToSurface(CoreNode node, Vector3 worldFaceNormal)
        {
            var worldRotation = RotationMath.ShortestArcRotation(Vector3.UnitZ, worldFaceNormal);
            node.LocalRotation = node.Parent is null
                ? worldRotation
                : Quaternion.Normalize(Quaternion.Inverse(node.Parent.GetWorldRotation()) * worldRotation);
        }

        // ---------------- rotate ----------------

        private void DragRotate(CoreNode node, Point mouse, ModifierKeys mods)
        {
            var angle = GizmoHandles.ScreenAngle(_projector, _startCenter, mouse);
            var step = angle - _previousAngle;
            if (step > Math.PI) step -= 2 * Math.PI;
            if (step < -Math.PI) step += 2 * Math.PI;
            _previousAngle = angle;
            _accumulatedAngle += step;

            var forward = CameraForward(_startCenter);
            var axis = _active == GizmoPart.View ? -forward : _startAxes[GizmoHandles.AxisIndex(_active)];
            // Screen y points down, so a clockwise screen drag is a positive screen angle.
            // Rotating about an axis pointing at the viewer must turn the same way the mouse does.
            var sign = Vector3.Dot(axis, forward) > 0 ? 1.0 : -1.0;
            var total = _accumulatedAngle * sign;
            if (mods.HasFlag(ModifierKeys.Control))
            {
                var stepRad = 15.0 * Math.PI / 180.0;
                total = Math.Round(total / stepRad) * stepRad;
            }

            var deltaRotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)total);
            var parentWorldRotation = node.Parent?.GetWorldRotation() ?? Quaternion.Identity;
            node.LocalRotation = Quaternion.Normalize(Quaternion.Inverse(parentWorldRotation) * deltaRotation * parentWorldRotation * _start.Rotation);
        }

        // ---------------- scale ----------------

        private void DragScale(CoreNode node, Point mouse, ModifierKeys mods)
        {
            if (_projector.WorldToScreen(_startCenter) is not { } c) return;
            float factor;
            if (_active == GizmoPart.View)
            {
                var d0 = (_startMouse - c).Length;
                factor = d0 < 1 ? 1 : (float)((mouse - c).Length / d0);
            }
            else
            {
                var axis = _startAxes[GizmoHandles.AxisIndex(_active)];
                if (_projector.WorldToScreen(_startCenter + axis * _projector.WorldUnitsPerPixel(_startCenter) * 50f) is not { } e) return;
                var dir = e - c;
                if (dir.Length < 1e-3) return;
                dir.Normalize();
                var a0 = System.Windows.Vector.Multiply(_startMouse - c, dir);
                var a1 = System.Windows.Vector.Multiply(mouse - c, dir);
                factor = Math.Abs(a0) < 2 ? 1 : (float)(a1 / a0);
            }

            if (mods.HasFlag(ModifierKeys.Control)) factor = MathF.Round(factor * 10f) / 10f;

            const float minimumScale = 0.001f;
            var s = _start.Scale;
            var result = _active switch
            {
                GizmoPart.AxisX => new Vector3(s.X * factor, s.Y, s.Z),
                GizmoPart.AxisY => new Vector3(s.X, s.Y * factor, s.Z),
                GizmoPart.AxisZ => new Vector3(s.X, s.Y, s.Z * factor),
                _ => s * factor,
            };
            node.LocalScale = new Vector3(
                CopySignMin(result.X, minimumScale),
                CopySignMin(result.Y, minimumScale),
                CopySignMin(result.Z, minimumScale));

            static float CopySignMin(float v, float min) => MathF.Abs(v) < min ? MathF.CopySign(min, v == 0 ? 1 : v) : v;
        }
    }
}
