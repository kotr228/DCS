using System.Numerics;
using JolieCat3D.Core.Constraints;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Tests
{
    public sealed class TransformConstraintTests
    {
        [Fact]
        public void CopyLocationFollowsChosenAxesInWorldSpaceUnderAParent()
        {
            var scene = new Scene3D();
            var parent = new Node("Parent") { LocalPosition = new Vector3(10, 0, 0), LocalScale = new Vector3(2) };
            var follower = new Node("Follower") { LocalPosition = new Vector3(1, 1, 1) };
            parent.AddChild(follower);
            var target = new Node("Target") { LocalPosition = new Vector3(4, 6, -2) };
            scene.AddRootNode(parent);
            scene.AddRootNode(target);

            follower.Constraints.Add(new CopyLocationConstraint { Target = target, Y = false });
            ConstraintSolver.Apply(scene);
            var world = follower.GetWorldPosition();
            Assert.Equal(4f, world.X, 4);
            Assert.Equal(2f, world.Y, 4);   // Y kept: parent offset 0 + local 1 * scale 2
            Assert.Equal(-2f, world.Z, 4);
        }

        [Fact]
        public void CopyRotationAndScaleBlendByInfluence()
        {
            var scene = new Scene3D();
            var node = new Node("A");
            var target = new Node("T") { LocalRotation = EulerAngles.FromDegrees(0, 90, 0), LocalScale = new Vector3(3) };
            scene.AddRootNode(node);
            scene.AddRootNode(target);
            node.Constraints.Add(new CopyRotationConstraint { Target = target, Influence = 0.5f });
            node.Constraints.Add(new CopyScaleConstraint { Target = target });
            ConstraintSolver.Apply(scene);
            Assert.Equal(45f, EulerAngles.ToDegrees(node.LocalRotation).Y, 1);
            Assert.Equal(3f, node.LocalScale.X, 3);
        }

        [Fact]
        public void LimitsClampOnlyEnabledAxes()
        {
            var scene = new Scene3D();
            var node = new Node("A") { LocalPosition = new Vector3(5, -5, 5), LocalRotation = EulerAngles.FromDegrees(0, 0, 80), LocalScale = new Vector3(0.1f, 4, 1) };
            scene.AddRootNode(node);
            var loc = new LimitLocationConstraint();
            loc.Use[0] = true; loc.Min[0] = -1; loc.Max[0] = 1;
            loc.Use[1] = true; loc.Min[1] = -2; loc.Max[1] = 2;
            var rot = new LimitRotationConstraint();
            rot.Use[2] = true; rot.Min[2] = -30; rot.Max[2] = 30;
            var scale = new LimitScaleConstraint();
            scale.Use[0] = true; scale.Min[0] = 0.5f; scale.Max[0] = 2;
            node.Constraints.Add(loc);
            node.Constraints.Add(rot);
            node.Constraints.Add(scale);
            ConstraintSolver.Apply(scene);
            Assert.Equal(new Vector3(1, -2, 5), node.LocalPosition);
            Assert.Equal(30f, EulerAngles.ToDegrees(node.LocalRotation).Z, 1);
            Assert.Equal(new Vector3(0.5f, 4, 1), node.LocalScale);
        }

        [Fact]
        public void ConstraintsSurviveSavingWithTheirTarget()
        {
            var scene = new Scene3D();
            var node = new Node("A");
            var target = new Node("T");
            scene.AddRootNode(node);
            scene.AddRootNode(target);
            node.Constraints.Add(new CopyLocationConstraint { Target = target, Z = false, Influence = 0.25f });
            var limit = new LimitRotationConstraint();
            limit.Use[1] = true; limit.Min[1] = -10; limit.Max[1] = 20;
            node.Constraints.Add(limit);

            var data = JolieCat3D.Service.Project.Jolie3DProjectSerializer.BuildData(scene, new JolieCat3D.Service.Animation.AnimationTimeline());
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            var (loaded, _) = JolieCat3D.Service.Project.Jolie3DProjectSerializer.FromData(System.Text.Json.JsonSerializer.Deserialize<JolieCat3D.Service.Project.ProjectFileData>(json)!);
            var a = loaded.Traverse().First(n => n.Name == "A");
            var copy = Assert.IsType<CopyLocationConstraint>(a.Constraints[0]);
            Assert.Equal("T", copy.Target?.Name);
            Assert.False(copy.Z);
            Assert.Equal(0.25f, copy.Influence);
            var l = Assert.IsType<LimitRotationConstraint>(a.Constraints[1]);
            Assert.True(l.Use[1]);
            Assert.Equal((-10f, 20f), (l.Min[1], l.Max[1]));
        }
    }
}
