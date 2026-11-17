using System.Numerics;
using JolieCat3D.Core.Constraints;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Project;

namespace JolieCat3D.Tests
{
    public sealed class ProjectSerializerTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "jc3d-tests-" + Guid.NewGuid().ToString("N"));

        public ProjectSerializerTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        private (Scene3D, AnimationTimeline) RoundTrip(Scene3D scene, AnimationTimeline timeline)
        {
            var path = Path.Combine(_dir, "scene.jolie3d");
            Jolie3DProjectSerializer.Save(scene, timeline, path);
            Assert.False(File.Exists(path + ".tmp"));
            return Jolie3DProjectSerializer.Load(path);
        }

        [Fact]
        public void EveryModifierTypeSurvivesRoundTrip()
        {
            var scene = new Scene3D("S");
            var cutter = new Node("Cube") { Mesh = Primitives.CreateCube() };
            var target = new Node("Cube") { Mesh = Primitives.CreateCube() }; // duplicate name on purpose
            target.Modifiers.Add(new MirrorModifier { Axis = MirrorAxis.Y, WeldThreshold = 0.01f });
            target.Modifiers.Add(new SubdivisionSurfaceModifier { Iterations = 2, IsEnabled = false });
            target.Modifiers.Add(new BooleanModifier { Operation = BooleanOperation.Difference, Target = cutter });
            target.Modifiers.Add(new ArrayModifier { Count = 5, RelativeOffset = new Vector3(0, 2, 0) });
            target.Modifiers.Add(new SolidifyModifier { Thickness = 0.25f });
            target.Modifiers.Add(new EdgeSplitModifier { AngleThresholdDegrees = 45f });
            target.Modifiers.Add(new DecimateModifier { Ratio = 0.3f, IsEnabled = false });
            target.Modifiers.Add(new ShrinkwrapModifier { Target = cutter, Mode = ShrinkwrapMode.Project, Offset = 0.05f });
            scene.AddRootNode(target);
            scene.AddRootNode(cutter);

            var (loaded, _) = RoundTrip(scene, new AnimationTimeline());

            var mods = loaded.RootNodes[0].Modifiers;
            Assert.Equal(8, mods.Count);
            Assert.Equal(MirrorAxis.Y, Assert.IsType<MirrorModifier>(mods[0]).Axis);
            var subsurf = Assert.IsType<SubdivisionSurfaceModifier>(mods[1]);
            Assert.Equal(2, subsurf.Iterations);
            Assert.False(subsurf.IsEnabled);
            var boolean = Assert.IsType<BooleanModifier>(mods[2]);
            Assert.Equal(BooleanOperation.Difference, boolean.Operation);
            // Must point at the SECOND "Cube", not the first one with the same name.
            Assert.Same(loaded.RootNodes[1], boolean.Target);
            var array = Assert.IsType<ArrayModifier>(mods[3]);
            Assert.Equal(5, array.Count);
            Assert.Equal(new Vector3(0, 2, 0), array.RelativeOffset);
            Assert.Equal(0.25f, Assert.IsType<SolidifyModifier>(mods[4]).Thickness);
            Assert.Equal(45f, Assert.IsType<EdgeSplitModifier>(mods[5]).AngleThresholdDegrees);
            var decimate = Assert.IsType<DecimateModifier>(mods[6]);
            Assert.Equal(0.3f, decimate.Ratio);
            Assert.False(decimate.IsEnabled);
            var shrinkwrap = Assert.IsType<ShrinkwrapModifier>(mods[7]);
            Assert.Same(loaded.RootNodes[1], shrinkwrap.Target);
            Assert.Equal(ShrinkwrapMode.Project, shrinkwrap.Mode);
            Assert.Equal(0.05f, shrinkwrap.Offset);
        }

        [Fact]
        public void CurveArmatureSkinAndConstraintsSurviveRoundTrip()
        {
            var scene = new Scene3D();

            var curve = new CurveData { Closed = true, BevelDepth = 0.2f, SegmentsPerSpan = 7, RadialSegments = 5 };
            curve.Points.Add(new CurvePoint(new Vector3(0, 0, 0), new Vector3(-1, 0, 0), new Vector3(1, 0, 0)));
            curve.Points.Add(new CurvePoint(new Vector3(3, 1, 0), new Vector3(2, 1, 0), new Vector3(4, 1, 0)));
            scene.AddRootNode(new Node("Curve") { Curve = curve, Mesh = curve.GenerateMesh() });

            var armature = new Node("Armature") { Armature = new ArmatureData() };
            var bone = new Node("Bone") { Bone = new BoneData { Head = Vector3.Zero, Tail = new Vector3(0, 2, 0) } };
            armature.AddChild(bone);
            bone.Bone!.CaptureRestPose(bone);
            scene.AddRootNode(armature);

            var skinned = new Node("Skinned") { Mesh = Primitives.CreateCube() };
            skinned.Mesh.SetVertexBoneWeights(0, new BoneIndices(0, 0, 0, 0), new Vector4(1, 0, 0, 0));
            var skin = new SkinBinding { Armature = armature };
            skin.Bones.Add(bone);
            skinned.SkinBinding = skin;
            skinned.Constraints.Add(new TrackToConstraint { Target = bone, ForwardAxis = ConstraintAxis.MinusY });
            scene.AddRootNode(skinned);

            var (loaded, _) = RoundTrip(scene, new AnimationTimeline());

            var loadedCurve = loaded.RootNodes[0].Curve!;
            Assert.True(loadedCurve.Closed);
            Assert.Equal(0.2f, loadedCurve.BevelDepth);
            Assert.Equal(7, loadedCurve.SegmentsPerSpan);
            Assert.Equal(5, loadedCurve.RadialSegments);
            Assert.Equal(new Vector3(4, 1, 0), loadedCurve.Points[1].HandleOut);

            var loadedArmature = loaded.RootNodes[1];
            Assert.NotNull(loadedArmature.Armature);
            var loadedBone = loadedArmature.Children[0];
            Assert.Equal(new Vector3(0, 2, 0), loadedBone.Bone!.Tail);
            Assert.Equal(bone.Bone.RestPoseWorldMatrix, loadedBone.Bone.RestPoseWorldMatrix);

            var loadedSkinned = loaded.RootNodes[2];
            Assert.Same(loadedArmature, loadedSkinned.SkinBinding!.Armature);
            Assert.Same(loadedBone, Assert.Single(loadedSkinned.SkinBinding.Bones));
            Assert.Equal(new Vector4(1, 0, 0, 0), loadedSkinned.Mesh!.Vertices[0].BoneWeights);

            var trackTo = Assert.IsType<TrackToConstraint>(Assert.Single(loadedSkinned.Constraints));
            Assert.Same(loadedBone, trackTo.Target);
            Assert.Equal(ConstraintAxis.MinusY, trackTo.ForwardAxis);
        }

        [Fact]
        public void MaterialSlotsSeamsAndPaintedTextureSurviveRoundTrip()
        {
            var mesh = Primitives.CreateCube();
            mesh.Material = new Material("Main") { DiffuseColor = new Color4(1, 0, 0), Roughness = 0.3f };
            var painted = new TextureBuffer(4, 2, new Color4(0, 0, 1));
            painted.SetPixel(3, 1, new Color4(1, 1, 0));
            mesh.Material.PaintedTextureBuffer = painted;
            mesh.AddMaterialSlot(new Material("Gold") { Metallic = 1f });
            if (mesh.Polygons.Count > 0) mesh.Polygons[0].MaterialSlotIndex = 0;
            mesh.MarkSeam(0, 1);
            mesh.MarkSeam(2, 3);

            var scene = new Scene3D();
            scene.AddRootNode(new Node("Box") { Mesh = mesh });

            var (loaded, _) = RoundTrip(scene, new AnimationTimeline());
            var loadedMesh = loaded.RootNodes[0].Mesh!;

            Assert.Equal(mesh.Vertices.Count, loadedMesh.Vertices.Count);
            Assert.Equal(mesh.Faces.Count, loadedMesh.Faces.Count);
            Assert.Equal(mesh.Polygons.Count, loadedMesh.Polygons.Count);
            Assert.Equal(0.3f, loadedMesh.Material!.Roughness);
            Assert.Equal("Gold", Assert.Single(loadedMesh.MaterialSlots).Name);
            if (mesh.Polygons.Count > 0) Assert.Equal(0, loadedMesh.Polygons[0].MaterialSlotIndex);
            Assert.True(loadedMesh.IsSeam(1, 0));
            Assert.True(loadedMesh.IsSeam(2, 3));
            Assert.Equal(2, loadedMesh.SeamEdges.Count);

            var tex = loadedMesh.Material.PaintedTextureBuffer!;
            Assert.Equal(4, tex.Width);
            Assert.Equal(2, tex.Height);
            Assert.Equal(new Color4(1, 1, 0), tex.GetPixel(3, 1));
            Assert.Equal(new Color4(0, 0, 1), tex.GetPixel(0, 0));
        }

        [Fact]
        public void CamerasLightsActiveCameraAndKeyframesSurviveRoundTrip()
        {
            var scene = new Scene3D("Shot");
            var cam = new Node("Cam") { Camera = new CameraData { ProjectionMode = CameraProjectionMode.Orthographic, OrthographicWidth = 7f } };
            var cam2 = new Node("Cam") { Camera = new CameraData { FieldOfView = 70f } };
            scene.AddRootNode(cam);
            scene.AddRootNode(cam2);
            scene.AddRootNode(new Node("Sun") { Light = new LightData { Type = LightType.Spot, SpotAngle = 30f, Intensity = 2f } });
            scene.ActiveCamera = cam2;

            var timeline = new AnimationTimeline();
            var track = timeline.GetOrCreateTrack(cam2);
            track.AddKeyframe(0, Vector3.Zero, Quaternion.Identity, Vector3.One);
            track.AddKeyframe(1, new Vector3(5, 0, 0), Quaternion.Identity, Vector3.One);

            var (loaded, loadedTimeline) = RoundTrip(scene, timeline);

            Assert.Same(loaded.RootNodes[1], loaded.ActiveCamera); // the second "Cam", not the first
            Assert.Equal(CameraProjectionMode.Orthographic, loaded.RootNodes[0].Camera!.ProjectionMode);
            Assert.Equal(70f, loaded.RootNodes[1].Camera!.FieldOfView);
            Assert.Equal(LightType.Spot, loaded.RootNodes[2].Light!.Type);
            Assert.Equal(30f, loaded.RootNodes[2].Light!.SpotAngle);
            Assert.NotEmpty(loadedTimeline.Tracks);
        }

        [Fact]
        public void VersionOneFileStillLoads()
        {
            const string json = """
            {"FormatVersion":1,"SceneName":"Old","RootNodes":[{"Name":"A","PositionX":1,"RotationW":1,"ScaleX":1,"ScaleY":1,"ScaleZ":1,
              "Modifiers":[{"Type":"Mirror","IsEnabled":true,"Axis":"Z"}],"Children":[]}],"ActiveCameraNodePath":null}
            """;
            var path = Path.Combine(_dir, "old.jolie3d");
            File.WriteAllText(path, json);

            var (scene, _) = Jolie3DProjectSerializer.Load(path);

            var node = Assert.Single(scene.RootNodes);
            Assert.Equal(1f, node.LocalPosition.X);
            Assert.Equal(MirrorAxis.Z, Assert.IsType<MirrorModifier>(Assert.Single(node.Modifiers)).Axis);
        }

        [Fact]
        public void NewerFormatIsRejectedWithClearError()
        {
            var path = Path.Combine(_dir, "future.jolie3d");
            File.WriteAllText(path, """{"FormatVersion":999,"SceneName":"X","RootNodes":[]}""");
            var ex = Assert.Throws<InvalidDataException>(() => Jolie3DProjectSerializer.Load(path));
            Assert.Contains("newer", ex.Message);
        }

        [Fact]
        public void GarbageFileIsRejected()
        {
            var path = Path.Combine(_dir, "bad.jolie3d");
            File.WriteAllText(path, "not json");
            Assert.Throws<InvalidDataException>(() => Jolie3DProjectSerializer.Load(path));
        }

        [Fact]
        public void CurveAndWeightedNormalModifiersSurviveRoundTrip()
        {
            var scene = new Scene3D("S");
            var path = new Node("Path") { Curve = new CurveData() };
            path.Curve.Points.Add(new CurvePoint(Vector3.Zero));
            path.Curve.Points.Add(new CurvePoint(new Vector3(4, 0, 0)));
            var rail = new Node("Rail") { Mesh = Primitives.CreateCube() };
            rail.Modifiers.Add(new CurveDeformModifier { Target = path });
            rail.Modifiers.Add(new WeightedNormalModifier { AngleDegrees = 45 });
            rail.Modifiers.Add(new BevelModifier { Width = 0.2f, Segments = 3, AngleDegrees = 40 });
            scene.AddRootNode(rail);
            scene.AddRootNode(path);

            var (loaded, _) = RoundTrip(scene, new AnimationTimeline());
            var mods = loaded.RootNodes[0].Modifiers;
            Assert.Same(loaded.RootNodes[1], Assert.IsType<CurveDeformModifier>(mods[0]).Target);
            Assert.Equal(45f, Assert.IsType<WeightedNormalModifier>(mods[1]).AngleDegrees);
            var bevel = Assert.IsType<BevelModifier>(mods[2]);
            Assert.Equal((0.2f, 3, 40f), (bevel.Width, bevel.Segments, bevel.AngleDegrees));
        }

        [Fact]
        public void CreasesSurviveRoundTrip()
        {
            var scene = new Scene3D("S");
            var mesh = Primitives.CreatePlane();
            var edge = mesh.GetEdges().First();
            mesh.MarkCrease(edge.Item1, edge.Item2);
            scene.AddRootNode(new Node("P") { Mesh = mesh });
            var (loaded, _) = RoundTrip(scene, new AnimationTimeline());
            Assert.True(loaded.RootNodes[0].Mesh!.IsCrease(edge.Item1, edge.Item2));
            Assert.Single(loaded.RootNodes[0].Mesh!.CreaseEdges);
        }

        [Fact]
        public void LinkedDuplicatesStayLinkedAfterLoading()
        {
            var scene = new Scene3D("S");
            var mesh = Primitives.CreateCube();
            var a = new Node("A") { Mesh = mesh };
            var b = new Node("B") { Mesh = mesh, LocalPosition = new Vector3(3, 0, 0) };
            var c = new Node("C") { Mesh = Primitives.CreateCube() };
            scene.AddRootNode(a);
            scene.AddRootNode(b);
            scene.AddRootNode(c);

            var (loaded, _) = RoundTrip(scene, new AnimationTimeline());
            Assert.Same(loaded.RootNodes[0].Mesh, loaded.RootNodes[1].Mesh);
            Assert.NotSame(loaded.RootNodes[0].Mesh, loaded.RootNodes[2].Mesh);
            Assert.Equal(mesh.Vertices.Count, loaded.RootNodes[1].Mesh!.Vertices.Count);
        }
    }
}
