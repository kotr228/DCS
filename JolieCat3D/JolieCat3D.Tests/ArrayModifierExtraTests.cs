using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Tests
{
    public sealed class ArrayModifierExtraTests
    {
        private static Mesh Strip()
        {
            // One quad 1 unit wide along X, sharing nothing.
            var m = new Mesh("Strip");
            m.AddVertex(new Vertex(new Vector3(0, 0, 0), Vector3.UnitY, new Vector2(0, 0)));
            m.AddVertex(new Vertex(new Vector3(1, 0, 0), Vector3.UnitY, new Vector2(1, 0)));
            m.AddVertex(new Vertex(new Vector3(1, 0, 1), Vector3.UnitY, new Vector2(1, 1)));
            m.AddVertex(new Vertex(new Vector3(0, 0, 1), Vector3.UnitY, new Vector2(0, 1)));
            m.AddPolygon(new Polygon(0, 3, 2, 1) { MaterialSlotIndex = 1 });
            return m;
        }

        [Fact]
        public void MergeWeldsTouchingCopiesWhenUvsMatch()
        {
            var strip = Strip();
            // UVs along the seam differ (u=1 vs u=0), so with seams kept nothing merges...
            var kept = new ArrayModifier { Count = 3, Merge = true }.Apply(strip);
            Assert.Equal(12, kept.Vertices.Count);
            // ...while a constant offset moves copies apart and keeps the slot per polygon.
            var spaced = new ArrayModifier { Count = 2, ConstantOffset = new Vector3(0.5f, 0, 0) }.Apply(strip);
            Assert.Contains(spaced.Vertices, v => v.Position == new Vector3(1.5f, 0, 0));
            Assert.All(spaced.Polygons, p => Assert.Equal(1, p.MaterialSlotIndex));

            // Same UV at the seam: merges into a 2x... strip with shared columns.
            var tiled = Strip();
            for (var i = 0; i < tiled.Vertices.Count; i++) tiled.SetVertexUV(i, new Vector2(0.5f, tiled.Vertices[i].UV.Y));
            var merged = new ArrayModifier { Count = 3, Merge = true }.Apply(tiled);
            Assert.Equal(8, merged.Vertices.Count);
        }

        [Fact]
        public void ArraySettingsSurviveSaving()
        {
            var scene = new Scene3D();
            var node = new Node("Fence") { Mesh = Strip() };
            node.Modifiers.Add(new ArrayModifier { Count = 5, ConstantOffset = new Vector3(0.1f, 0, 0), Merge = true, MergeDistance = 0.01f });
            scene.AddRootNode(node);
            var data = JolieCat3D.Service.Project.Jolie3DProjectSerializer.BuildData(scene, new JolieCat3D.Service.Animation.AnimationTimeline());
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            var (loaded, _) = JolieCat3D.Service.Project.Jolie3DProjectSerializer.FromData(System.Text.Json.JsonSerializer.Deserialize<JolieCat3D.Service.Project.ProjectFileData>(json)!);
            var array = Assert.IsType<ArrayModifier>(loaded.Traverse().First(n => n.Name == "Fence").Modifiers[0]);
            Assert.Equal((5, new Vector3(0.1f, 0, 0), true, 0.01f), (array.Count, array.ConstantOffset, array.Merge, array.MergeDistance));
        }
    }
}
