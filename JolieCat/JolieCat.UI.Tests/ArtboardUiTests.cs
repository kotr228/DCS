using System.IO;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ArtboardUiTests
    {
        private static void Sta(Action test)
        {
            Exception? error = null;
            var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }

        [Fact]
        public void AddUndoAndRender_Artboards() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.StandardImage, 100, 60, null);
            var layers = doc.Layers;
            layers.Scene.Layers[0].Canvas.Clear(SKColors.Red);
            using (var blue = new SKPaint { Color = SKColors.Blue }) layers.Scene.Layers[0].Canvas.DrawRect(50, 0, 50, 60, blue);

            Assert.True(layers.AddArtboard("Left", SKRectI.Create(0, 0, 50, 60)));
            Assert.True(layers.AddArtboard("Left", SKRectI.Create(50, 10, 80, 20)));   // same name, too wide
            Assert.Equal(new[] { "Left", "Left 2" }, layers.Artboards.Select(a => a.Name));
            Assert.Equal(SKRectI.Create(50, 10, 50, 20), layers.Artboards[1].Bounds);  // cut to the canvas
            Assert.False(layers.AddArtboard("Off", SKRectI.Create(200, 0, 10, 10)));

            var pictures = layers.RenderArtboards();
            Assert.Equal(SKColors.Red, pictures[0].Picture.GetPixel(10, 10));
            Assert.Equal(SKColors.Blue, pictures[1].Picture.GetPixel(10, 10));
            Assert.Equal(50, pictures[1].Picture.Width);
            foreach (var (_, p) in pictures) p.Dispose();

            layers.History.Undo();
            Assert.Single(layers.Artboards);
            layers.History.Redo();
            Assert.Equal(2, layers.Artboards.Count);
            layers.RemoveArtboard("Left");
            Assert.Equal("Left 2", Assert.Single(layers.Artboards).Name);
            layers.History.Undo();
            Assert.Equal(2, layers.Artboards.Count);

            layers.RotateDocument(2);                                   // structural undo restores them too
            Assert.Equal(SKRectI.Create(50, 0, 50, 60), layers.Artboards[0].Bounds);
            layers.History.Undo();
            Assert.Equal(SKRectI.Create(0, 0, 50, 60), layers.Artboards[0].Bounds);
        });

        [Fact]
        public void MoveTool_DragsAnArtboardByItsLabel() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "D", ProjectType.StandardImage, 400, 300, null);
            var canvas = doc.Canvas;
            doc.Layers.AddArtboard("Box", SKRectI.Create(100, 100, 50, 50));
            toolbox.SelectTool(JolieCat.Shared.Enums.ToolType.Move);
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));

            var label = UI.ViewModels.CanvasViewModel.ArtboardLabelRect(doc.Layers.Artboards[0], canvas.Zoom);
            var grab = new SKPoint(label.Left + 2, label.MidY);
            canvas.OnPointerPressed(Device(grab.X, grab.Y));
            canvas.OnPointerMoved(Device(grab.X + 30, grab.Y + 20));
            canvas.OnPointerReleased(Device(grab.X + 30, grab.Y + 20));
            Assert.Equal(SKRectI.Create(130, 120, 50, 50), doc.Layers.Artboards[0].Bounds);

            canvas.OnPointerPressed(Device(label.Left + 32, label.MidY + 20));
            canvas.OnPointerMoved(Device(label.Left + 2000, label.MidY + 20));      // kept on the canvas
            canvas.OnPointerReleased(Device(label.Left + 2000, label.MidY + 20));
            Assert.Equal(350, doc.Layers.Artboards[0].Bounds.Left);

            doc.Layers.History.Undo();
            Assert.Equal(130, doc.Layers.Artboards[0].Bounds.Left);
            doc.Layers.History.Undo();
            Assert.Equal(100, doc.Layers.Artboards[0].Bounds.Left);
        });
    }
}
