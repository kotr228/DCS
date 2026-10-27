using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    /// <summary>Layer groups as the Layers panel and canvas see them.</summary>
    public class LayerGroupPanelTests
    {
        private static void Sta(Action test)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { test(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }

        private static (ToolboxViewModel Toolbox, DocumentViewModel Document) NewDocument()
        {
            var toolbox = new ToolboxViewModel();
            var document = new DocumentViewModel(toolbox, "Test");
            document.Canvas.SetViewportSize(800, 600);
            document.Layers.ActiveLayer!.Model.Canvas.Clear(SKColors.White);
            return (toolbox, document);
        }

        [Fact]
        public void GroupLayers_ShowsTheLayerIndentedUnderTheGroup() => Sta(() =>
        {
            var (_, document) = NewDocument();
            var layers = document.Layers;
            layers.AddLayerCommand.Execute(null);           // "Layer 2"
            layers.GroupLayersCommand.Execute(null);        // Ctrl+G

            Assert.Equal(new[] { "Group 1", "Layer 2", "Background" }, layers.Items.Select(i => i.Name));
            Assert.Equal(new[] { 0, 1, 0 }, layers.Items.Select(i => i.Depth));
            Assert.True(layers.ActiveLayer!.IsGroup);
            Assert.Equal("Group Layers", layers.History.UndoNames[^1]);

            layers.History.Undo();
            Assert.Equal(new[] { "Layer 2", "Background" }, layers.Items.Select(i => i.Name));
        });

        [Fact]
        public void CollapsingAGroup_HidesItsRows_AndSelectsTheGroup() => Sta(() =>
        {
            var (_, document) = NewDocument();
            var layers = document.Layers;
            layers.AddLayerCommand.Execute(null);
            var member = layers.ActiveLayer!.Model;
            layers.GroupLayersCommand.Execute(null);
            layers.ActiveLayer = layers.Items.First(i => i.Model == member);

            layers.Items[0].IsExpanded = false;

            Assert.Equal(new[] { "Group 1", "Background" }, layers.Items.Select(i => i.Name));
            Assert.True(layers.ActiveLayer!.IsGroup);
        });

        [Fact]
        public void NewLayer_GoesAboveTheActiveLayer_InsideItsGroup() => Sta(() =>
        {
            var (_, document) = NewDocument();
            var layers = document.Layers;
            layers.AddLayerCommand.Execute(null);
            var member = layers.ActiveLayer!.Model;
            layers.GroupLayersCommand.Execute(null);
            layers.ActiveLayer = layers.Items.First(i => i.Model == member);

            layers.AddLayerCommand.Execute(null);

            var added = layers.ActiveLayer!.Model;
            Assert.Equal(member.ParentId, added.ParentId);
            Assert.Equal(layers.Scene.IndexOf(member) + 1, layers.Scene.IndexOf(added));
        });

        [Fact]
        public void PaintingWithAGroupSelected_ChangesNothing() => Sta(() =>
        {
            var (toolbox, document) = NewDocument();
            var layers = document.Layers;
            layers.NewGroupCommand.Execute(null);
            var steps = layers.History.UndoCount;

            toolbox.SelectTool(ToolType.Brush);
            var canvas = document.Canvas;
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            canvas.OnPointerPressed(Device(100, 100));
            canvas.OnPointerMoved(Device(200, 100));
            canvas.OnPointerReleased(Device(200, 100));

            Assert.Equal(steps, layers.History.UndoCount);
            Assert.Contains("group", canvas.ToolHintText, StringComparison.OrdinalIgnoreCase);
        });

        [Fact]
        public void PaintingInsideAGroup_StillRendersThroughTheCache() => Sta(() =>
        {
            var (toolbox, document) = NewDocument();
            var layers = document.Layers;
            layers.AddLayerCommand.Execute(null);
            var member = layers.ActiveLayer!.Model;
            layers.GroupLayersCommand.Execute(null);
            layers.Items[0].Model.Opacity = 0.5;
            layers.ActiveLayer = layers.Items.First(i => i.Model == member);

            toolbox.SelectTool(ToolType.Brush);
            document.Canvas.PrimaryColor = System.Windows.Media.Colors.Red;
            var canvas = document.Canvas;
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            canvas.OnPointerPressed(Device(100, 100));
            canvas.OnPointerMoved(Device(300, 100));
            canvas.OnPointerReleased(Device(300, 100));

            Assert.Equal(255, member.Bitmap.GetPixel(200, 100).Red);
            using var flat = Core.Rendering.SceneCompositor.Flatten(layers.Scene, layers.DocumentWidth, layers.DocumentHeight);
            Assert.InRange(flat.GetPixel(200, 100).Green, 110, 145); // red at the group's 50% over white
        });
    
        [Fact]
        public void CommandsActOnTheMultiSelection() => Sta(() =>
        {
            var (_, document) = NewDocument();
            var layers = document.Layers;
            layers.AddLayerCommand.Execute(null);
            var a = layers.ActiveLayer!.Model;
            layers.AddLayerCommand.Execute(null);
            var b = layers.ActiveLayer!.Model;

            layers.SetSelectedLayers(new[] { a, b });
            layers.GroupLayersCommand.Execute(null);

            var group = layers.ActiveLayer!.Model;
            Assert.True(group.IsGroup);
            Assert.Equal(group.Id, a.ParentId);
            Assert.Equal(group.Id, b.ParentId);
            Assert.Equal(1, layers.History.UndoNames.Count(n => n == "Group Layers"));

            // A selection the active layer isn't part of is ignored.
            layers.SetSelectedLayers(new[] { a, b });
            layers.DeleteLayerCommand.Execute(null); // active = the group: deletes it (and a, b)
            Assert.Equal(new[] { "Background" }, layers.Items.Select(i => i.Name));
        });

        [Fact]
        public void DragAndDrop_MoveIsOneUndoStep() => Sta(() =>
        {
            var (_, document) = NewDocument();
            var layers = document.Layers;
            layers.AddLayerCommand.Execute(null);
            var layer = layers.ActiveLayer!.Model;
            var background = layers.Scene.Layers[0];

            Assert.True(layers.MoveLayers(layer, background, Core.Documents.LayerDropPosition.Below));

            Assert.Equal(new[] { "Background", "Layer 2" }, layers.Items.Select(i => i.Name));
            Assert.Equal("Move Layer", layers.History.UndoNames[^1]);
            layers.History.Undo();
            Assert.Equal(new[] { "Layer 2", "Background" }, layers.Items.Select(i => i.Name));
        });

        [Fact]
        public void MoveTool_WithAGroupActive_MovesAllItsLayers_AsOneUndoStep() => Sta(() =>
        {
            var (toolbox, document) = NewDocument();
            var layers = document.Layers;
            layers.AddLayerCommand.Execute(null);
            var a = layers.ActiveLayer!.Model;
            TestFill(a, 100, 100);
            layers.AddLayerCommand.Execute(null);
            var b = layers.ActiveLayer!.Model;
            TestFill(b, 300, 100);
            layers.SetSelectedLayers(new[] { a, b });
            layers.GroupLayersCommand.Execute(null);
            var steps = layers.History.UndoCount;

            toolbox.SelectTool(ToolType.Move);
            var canvas = document.Canvas;
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            canvas.OnPointerPressed(Device(500, 500));
            canvas.OnPointerMoved(Device(550, 520));
            canvas.OnPointerReleased(Device(550, 520));

            Assert.Equal(255, a.Bitmap.GetPixel(155, 125).Red);
            Assert.Equal(0, a.Bitmap.GetPixel(105, 105).Alpha);
            Assert.Equal(255, b.Bitmap.GetPixel(355, 125).Red);
            Assert.Equal(steps + 1, layers.History.UndoCount);

            layers.History.Undo();
            Assert.Equal(255, a.Bitmap.GetPixel(105, 105).Red);
            Assert.Equal(255, b.Bitmap.GetPixel(305, 105).Red);
        });

        [Fact]
        public void PaintingOnAGroupMask_HidesTheGroup() => Sta(() =>
        {
            var (toolbox, document) = NewDocument();
            var layers = document.Layers;
            layers.AddLayerCommand.Execute(null);
            var member = layers.ActiveLayer!.Model;
            member.Canvas.Clear(SKColors.Red);
            layers.GroupLayersCommand.Execute(null);
            var group = layers.ActiveLayer!.Model;

            layers.AddMaskCommand.Execute(null);
            Assert.True(group.IsMaskActive);

            toolbox.SelectTool(ToolType.Brush);
            if (toolbox.CurrentToolOptions is JolieCat.UI.ViewModels.ToolOptions.PaintToolOptionsViewModel options) { options.Size = 60; options.Hardness = 100; }
            document.Canvas.PrimaryColor = System.Windows.Media.Colors.Black;
            var canvas = document.Canvas;
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            canvas.OnPointerPressed(Device(100, 100));
            canvas.OnPointerMoved(Device(300, 100));
            canvas.OnPointerReleased(Device(300, 100));

            Assert.Equal(SKColors.Red, member.Bitmap.GetPixel(200, 100)); // the member itself is untouched
            using var flat = Core.Rendering.SceneCompositor.Flatten(layers.Scene, layers.DocumentWidth, layers.DocumentHeight);
            Assert.Equal(SKColors.White, flat.GetPixel(200, 100));
            Assert.Equal(SKColors.Red, flat.GetPixel(200, 400));
        });

        private static void TestFill(Core.Documents.Layer layer, int x, int y)
        {
            using var red = new SKPaint { Color = SKColors.Red };
            layer.Canvas.DrawRect(x, y, 20, 20, red);
        }
    }
}
