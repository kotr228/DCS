using System.Numerics;
using JolieCat3D.Core.Constraints;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Project;

namespace JolieCat3D.Tests
{
    public sealed class InverseKinematicsTests
    {
        /// <summary>Armature with two 1-unit bones pointing up (+Y), plus an IK on the tip bone.</summary>
        private static (Scene3D Scene, Node Upper, Node Lower, Node Target, InverseKinematicsConstraint Ik) Arm()
        {
            var scene = new Scene3D();
            var armature = new Node("Armature") { Armature = new ArmatureData() };
            var upper = new Node("Upper") { Bone = new BoneData() };
            var lower = new Node("Lower") { Bone = new BoneData(), LocalPosition = new Vector3(0, 1, 0) };
            armature.AddChild(upper);
            upper.AddChild(lower);
            var target = new Node("Target") { LocalPosition = new Vector3(1.2f, 0.8f, 0) };
            scene.AddRootNode(armature);
            scene.AddRootNode(target);
            var ik = new InverseKinematicsConstraint { Target = target, ChainLength = 2 };
            lower.Constraints.Add(ik);
            return (scene, upper, lower, target, ik);
        }

        private static Vector3 Tip(Node lower) => Vector3.Transform(lower.Bone!.Tail, lower.GetWorldTransform());

        [Fact]
        public void ReachableTargetIsReached()
        {
            var (scene, _, lower, target, _) = Arm();
            ConstraintSolver.Apply(scene);
            Assert.True(Vector3.Distance(Tip(lower), target.GetWorldPosition()) < 1e-3f, $"tip {Tip(lower)} target {target.GetWorldPosition()} elbow {lower.GetWorldPosition()}");
        }

        [Fact]
        public void BonesKeepTheirLength()
        {
            var (scene, upper, lower, _, _) = Arm();
            ConstraintSolver.Apply(scene);
            Assert.Equal(1f, Vector3.Distance(upper.GetWorldPosition(), lower.GetWorldPosition()), 4);
            Assert.Equal(1f, Vector3.Distance(lower.GetWorldPosition(), Tip(lower)), 4);
        }

        [Fact]
        public void UnreachableTargetStretchesTowardsIt()
        {
            var (scene, upper, lower, target, _) = Arm();
            target.LocalPosition = new Vector3(10, 0, 0);
            ConstraintSolver.Apply(scene);
            var direction = Vector3.Normalize(Tip(lower) - upper.GetWorldPosition());
            Assert.True(Vector3.Dot(direction, Vector3.UnitX) > 0.99f);
            Assert.Equal(2f, Vector3.Distance(upper.GetWorldPosition(), Tip(lower)), 2);
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(-1f)]
        public void PoleChoosesWhichWayTheElbowBends(float side)
        {
            var (scene, _, lower, target, ik) = Arm();
            target.LocalPosition = new Vector3(0, 1.4f, 0); // straight up, so the elbow must bend sideways
            var pole = new Node("Pole") { LocalPosition = new Vector3(0, 0.7f, side * 3) };
            scene.AddRootNode(pole);
            ik.PoleTarget = pole;

            ConstraintSolver.Apply(scene);

            Assert.True(Vector3.Distance(Tip(lower), target.GetWorldPosition()) < 1e-2f);
            Assert.True(lower.GetWorldPosition().Z * side > 0.1f, $"elbow z = {lower.GetWorldPosition().Z}");
        }

        [Fact]
        public void LongerChainsConvergeWithCcd()
        {
            var scene = new Scene3D();
            var armature = new Node("Armature") { Armature = new ArmatureData() };
            scene.AddRootNode(armature);
            Node parent = armature;
            Node tip = armature;
            for (var i = 0; i < 4; i++)
            {
                var bone = new Node($"B{i}") { Bone = new BoneData(), LocalPosition = i == 0 ? Vector3.Zero : new Vector3(0, 1, 0) };
                parent.AddChild(bone);
                parent = bone;
                tip = bone;
            }
            var target = new Node("Target") { LocalPosition = new Vector3(2, 1.5f, 1) };
            scene.AddRootNode(target);
            tip.Constraints.Add(new InverseKinematicsConstraint { Target = target, ChainLength = 4 });

            ConstraintSolver.Apply(scene);

            Assert.True(Vector3.Distance(Tip(tip), target.GetWorldPosition()) < 1e-2f, $"tip {Tip(tip)}");
        }

        [Fact]
        public void IkSurvivesProjectSaveAndLoad()
        {
            var (scene, _, _, _, ik) = Arm();
            ik.ChainLength = 3;
            ik.Iterations = 12;
            var data = Jolie3DProjectSerializer.BuildData(scene, new AnimationTimeline());
            var (loaded, _) = Jolie3DProjectSerializer.FromData(System.Text.Json.JsonSerializer.Deserialize<ProjectFileData>(System.Text.Json.JsonSerializer.Serialize(data))!);
            var lower = loaded.Traverse().First(n => n.Name == "Lower");
            var loadedIk = Assert.IsType<InverseKinematicsConstraint>(Assert.Single(lower.Constraints));
            Assert.Equal(3, loadedIk.ChainLength);
            Assert.Equal(12, loadedIk.Iterations);
            Assert.Same(loaded.Traverse().First(n => n.Name == "Target"), loadedIk.Target);
        }

        [Fact]
        public void BakingIkIntoKeysReproducesThePoseWithoutConstraints()
        {
            var (scene, _, lower, target, _) = Arm();
            var timeline = new AnimationTimeline { FrameRate = 10 };
            // Animate the IK target from one reachable spot to another over frames 0..10.
            var targetTrack = timeline.GetOrCreateTrack(target);
            targetTrack.AddKeyframe(0, new Vector3(1.2f, 0.8f, 0), Quaternion.Identity, Vector3.One);
            targetTrack.AddKeyframe(1, new Vector3(-0.8f, 1.2f, 0), Quaternion.Identity, Vector3.One);

            var result = ConstraintBaker.Bake(scene, timeline, 0, 10);
            Assert.Equal(11, result.Frames);
            Assert.Contains(lower, result.BakedNodes);
            Assert.Empty(lower.Constraints);

            // Without the IK, the keys alone put the tip on the (moving) target.
            foreach (var frame in new[] { 0, 5, 10 })
            {
                timeline.SetTime(frame / 10.0);
                timeline.Apply();
                Assert.True(Vector3.Distance(Tip(lower), target.GetWorldPosition()) < 0.02f, $"frame {frame}: tip {Tip(lower)} target {target.GetWorldPosition()}");
            }
        }
    }
}
