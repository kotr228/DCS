using JolieCat.Core.Documents;
using JolieCat.Core.Formats;
using JolieCat.Core.Rendering;
using JolieCat.Core.Serialization;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class LayerGroupTests
    {
        private const int W = 10, H = 10;

        /// <summary>White background, then a group holding a red layer (left half)
        /// and a blue layer (right half), then a top layer "Top".</summary>
        private static Scene GroupedScene(out Layer group, out Layer red, out Layer blue, out Layer top)
        {
            var scene = new Scene("S");
            scene.AddLayer("Bg", W, H).Canvas.Clear(SKColors.White);
            red = scene.AddLayer("Red", W, H);
            TestHelpers.FillRect(red, new SKRect(0, 0, 5, 10), SKColors.Red);
            blue = scene.AddLayer("Blue", W, H);
            TestHelpers.FillRect(blue, new SKRect(5, 0, 10, 10), SKColors.Blue);
            top = scene.AddLayer("Top", W, H);

            group = scene.GroupLayer(red, "G");
            blue.ParentId = group.Id;
            // GroupLayer put the group right above Red; move Blue under it.
            var list = (List<Layer>)typeof(Scene).GetField("_layers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
            list.Remove(group);
            list.Insert(list.IndexOf(blue) + 1, group);
            return scene;
        }

        private static string Order(Scene scene) => string.Join(",", scene.Layers.Select(l => l.Name));

        [Fact]
        public void Structure_IsReadCorrectly()
        {
            using var scene = GroupedScene(out var group, out var red, out var blue, out var top);

            Assert.Equal("Bg,Red,Blue,G,Top", Order(scene));
            Assert.Equal((1, 3), scene.BlockOf(group));
            Assert.Equal(1, scene.Depth(red));
            Assert.Equal(0, scene.Depth(top));
            Assert.True(scene.IsDescendantOf(blue, group));
        }

        [Fact]
        public void HiddenGroup_HidesItsMembers()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);
            group.IsVisible = false;

            using var flat = SceneCompositor.Flatten(scene, W, H);
            Assert.Equal(SKColors.White, flat.GetPixel(2, 2));
            Assert.Equal(SKColors.White, flat.GetPixel(8, 2));
        }

        [Fact]
        public void GroupOpacity_AppliesToTheMembersTogether()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);
            group.Opacity = 0.5;

            using var flat = SceneCompositor.Flatten(scene, W, H);
            var c = flat.GetPixel(2, 2);
            Assert.InRange(c.Red, 250, 255);
            Assert.InRange(c.Green, 120, 135); // red at 50% over white
        }

        [Fact]
        public void GroupBlendMode_BlendsTheMembersAsOne()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);
            scene.Layers[0].Canvas.Clear(new SKColor(128, 128, 128));
            group.BlendMode = Shared.Enums.BlendMode.Multiply;

            using var flat = SceneCompositor.Flatten(scene, W, H);
            var c = flat.GetPixel(2, 2);
            Assert.InRange(c.Red, 120, 135);  // red multiplied with gray
            Assert.InRange(c.Green, 0, 5);
        }

        [Fact]
        public void MovingUp_FromTheTopOfAGroup_LeavesIt()
        {
            using var scene = GroupedScene(out var group, out _, out var blue, out _);

            Assert.True(scene.MoveLayerUp(blue));

            Assert.Equal("Bg,Red,G,Blue,Top", Order(scene));
            Assert.Null(blue.ParentId);
        }

        [Fact]
        public void MovingDown_IntoAGroup_EntersItAtTheTop()
        {
            using var scene = GroupedScene(out var group, out _, out _, out var top);

            Assert.True(scene.MoveLayerDown(top));

            Assert.Equal("Bg,Red,Blue,Top,G", Order(scene));
            Assert.Equal(group.Id, top.ParentId);
        }

        [Fact]
        public void MovingAGroup_MovesItsMembersWithIt()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);

            Assert.True(scene.MoveLayerDown(group));   // below the background
            Assert.Equal("Red,Blue,G,Bg,Top", Order(scene));
            Assert.Equal((0, 2), scene.BlockOf(group));
        }

        [Fact]
        public void DeletingAGroup_DeletesItsMembers()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);

            scene.RemoveLayer(group);

            Assert.Equal("Bg,Top", Order(scene));
        }

        [Fact]
        public void Ungroup_KeepsTheMembers()
        {
            using var scene = GroupedScene(out var group, out var red, out var blue, out _);

            scene.Ungroup(group);

            Assert.Equal("Bg,Red,Blue,Top", Order(scene));
            Assert.Null(red.ParentId);
            Assert.Null(blue.ParentId);
        }

        [Fact]
        public void DuplicateGroup_CopiesTheWholeBlockWithNewLinks()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);

            var copy = scene.DuplicateLayer(group)!;

            Assert.Equal("Bg,Red,Blue,G,Red,Blue,G copy,Top", Order(scene));
            Assert.Equal((4, 6), scene.BlockOf(copy));
            Assert.Equal(copy.Id, scene.Layers[4].ParentId);
            Assert.Equal(group.Id, scene.Layers[1].ParentId);
        }

        [Fact]
        public void MergeGroup_BecomesOneLayerThatLooksTheSame()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);
            group.Opacity = 0.5;
            using var before = SceneCompositor.Flatten(scene, W, H);

            Assert.True(scene.MergeLayerDown(group));

            Assert.Equal("Bg,G,Top", Order(scene));
            Assert.False(scene.Layers[1].IsGroup);
            using var after = SceneCompositor.Flatten(scene, W, H);
            Assert.Equal(before.GetPixel(2, 2), after.GetPixel(2, 2));
            Assert.Equal(before.GetPixel(8, 2), after.GetPixel(8, 2));
        }

        [Fact]
        public void MergeDown_DoesNotMergeIntoOrOutOfAGroup()
        {
            using var scene = GroupedScene(out _, out var red, out _, out var top);

            Assert.False(scene.MergeLayerDown(top));   // the layer below is a group
            Assert.False(scene.MergeLayerDown(red));   // the layer below is outside the group
        }

        [Fact]
        public void Groups_SurviveUndoSnapshots()
        {
            using var scene = GroupedScene(out var group, out var red, out _, out _);
            group.IsExpanded = false;

            scene.RestoreLayers(scene.CaptureLayers(), 0);

            var restoredGroup = scene.Layers[3];
            Assert.True(restoredGroup.IsGroup);
            Assert.False(restoredGroup.IsExpanded);
            Assert.Equal(restoredGroup.Id, scene.Layers[1].ParentId);
        }

        [Fact]
        public void Groups_SurviveProjectSave()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);
            group.Opacity = 0.4;

            var folder = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "g.jolie");
                ProjectSerializer.Save(path, scene, Array.Empty<TimelineTrackData>(), 0, 0);
                var loaded = ProjectSerializer.Load(path).Scene;
                using (loaded)
                {
                    Assert.Equal("Bg,Red,Blue,G,Top", Order(loaded));
                    var g = loaded.Layers[3];
                    Assert.True(g.IsGroup);
                    Assert.Equal(0.4, g.Opacity, 3);
                    Assert.Equal(g.Id, loaded.Layers[1].ParentId);
                    Assert.Equal(g.Id, loaded.Layers[2].ParentId);
                    Assert.Null(loaded.Layers[4].ParentId);
                }
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void Groups_SurvivePsdRoundTrip()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);
            group.IsVisible = false;

            using var stream = new MemoryStream();
            PsdCodec.Write(stream, scene, W, H);
            stream.Position = 0;
            var read = PsdCodec.Read(stream);
            using (read.Scene)
            {
                Assert.Equal("Bg,Red,Blue,G,Top", Order(read.Scene));
                var g = read.Scene.Layers[3];
                Assert.True(g.IsGroup);
                Assert.False(g.IsVisible);
                Assert.Equal(g.Id, read.Scene.Layers[1].ParentId);
                Assert.Equal(g.Id, read.Scene.Layers[2].ParentId);
            }
        }

        [Fact]
        public void Normalize_DropsBrokenLinks()
        {
            using var scene = GroupedScene(out var group, out _, out _, out var top);
            top.ParentId = group.Id; // Top isn't below the group - not a real member

            scene.NormalizeGroups();

            Assert.Null(top.ParentId);
        }

        [Fact]
        public void MoveLayers_IntoAGroup_GoesToItsTop()
        {
            using var scene = GroupedScene(out var group, out _, out _, out var top);

            Assert.True(scene.MoveLayers(new[] { top }, group, LayerDropPosition.Into));

            Assert.Equal("Bg,Red,Blue,Top,G", Order(scene));
            Assert.Equal(group.Id, top.ParentId);
        }

        [Fact]
        public void MoveLayers_SeveralAtOnce_KeepTheirOrder()
        {
            using var scene = GroupedScene(out var group, out var red, out _, out var top);
            var bg = scene.Layers[0];

            // Background and Top, dropped below Red (inside the group).
            Assert.True(scene.MoveLayers(new[] { top, bg }, red, LayerDropPosition.Below));

            Assert.Equal("Bg,Top,Red,Blue,G", Order(scene));
            Assert.Equal(group.Id, bg.ParentId);
            Assert.Equal(group.Id, top.ParentId);
        }

        [Fact]
        public void MoveLayers_RefusesToPutAGroupInsideItself()
        {
            using var scene = GroupedScene(out var group, out var red, out _, out _);

            Assert.False(scene.MoveLayers(new[] { group }, red, LayerDropPosition.Above));
            Assert.Equal("Bg,Red,Blue,G,Top", Order(scene));
        }

        [Fact]
        public void GroupLayers_Several_MakesOneGroupWhereTheFrontOneWas()
        {
            using var scene = GroupedScene(out var group, out var red, out _, out var top);
            var bg = scene.Layers[0];

            var newGroup = scene.GroupLayers(new[] { bg, top }, "New");

            Assert.Equal("Red,Blue,G,Bg,Top,New", Order(scene));
            Assert.Equal(newGroup.Id, bg.ParentId);
            Assert.Equal(newGroup.Id, top.ParentId);
            Assert.Null(newGroup.ParentId);
        }

        [Fact]
        public void MergeLayers_Several_LooksTheSame()
        {
            using var scene = GroupedScene(out var group, out var red, out var blue, out _);
            red.Opacity = 0.5;
            using var before = SceneCompositor.Flatten(scene, W, H);

            var merged = scene.MergeLayers(new[] { red, blue })!;

            Assert.Equal("Bg,Blue,G,Top", Order(scene));
            Assert.Equal(group.Id, merged.ParentId);
            using var after = SceneCompositor.Flatten(scene, W, H);
            Assert.Equal(before.GetPixel(2, 2), after.GetPixel(2, 2));
            Assert.Equal(before.GetPixel(8, 2), after.GetPixel(8, 2));
        }

        [Fact]
        public void RemoveLayers_Several()
        {
            using var scene = GroupedScene(out var group, out var red, out _, out var top);

            scene.RemoveLayers(new[] { red, top });

            Assert.Equal("Bg,Blue,G", Order(scene));
        }

        [Fact]
        public void GroupMask_HidesPartOfEveryMember()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);
            var mask = group.AddMask();
            using (var black = new SKPaint { Color = SKColors.Black })
                mask.Canvas.DrawRect(0, 0, 10, 5, black); // hide the top half

            using var flat = SceneCompositor.Flatten(scene, W, H);
            Assert.Equal(SKColors.White, flat.GetPixel(2, 2));
            Assert.Equal(SKColors.White, flat.GetPixel(8, 2));
            Assert.Equal(SKColors.Red, flat.GetPixel(2, 8));
            Assert.Equal(SKColors.Blue, flat.GetPixel(8, 8));
        }

        [Fact]
        public void GroupMask_SurvivesMergeGroupAndPsd()
        {
            using var scene = GroupedScene(out var group, out _, out _, out _);
            var mask = group.AddMask();
            using (var black = new SKPaint { Color = SKColors.Black })
                mask.Canvas.DrawRect(0, 0, 10, 5, black);

            using var stream = new MemoryStream();
            PsdCodec.Write(stream, scene, W, H);
            stream.Position = 0;
            var read = PsdCodec.Read(stream);
            using (read.Scene)
            {
                Assert.NotNull(read.Scene.Layers[3].Mask);
                using var flatRead = SceneCompositor.Flatten(read.Scene, W, H);
                Assert.Equal(SKColors.White, flatRead.GetPixel(2, 2));
                Assert.Equal(SKColors.Red, flatRead.GetPixel(2, 8));
            }

            scene.MergeLayerDown(group);
            using var flat = SceneCompositor.Flatten(scene, W, H);
            Assert.Equal(SKColors.White, flat.GetPixel(2, 2));
            Assert.Equal(SKColors.Red, flat.GetPixel(2, 8));
        }
    }
}
