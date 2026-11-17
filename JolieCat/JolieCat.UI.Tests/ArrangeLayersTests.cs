using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ArrangeLayersTests
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

        private static Core.Documents.Layer Box(DocumentViewModel doc, int x, int y)
        {
            doc.Layers.AddLayerCommand.Execute(null);
            var layer = doc.Layers.ActiveLayer!.Model;
            using var paint = new SKPaint { Color = SKColors.Red };
            layer.Canvas.DrawRect(x, y, 10, 10, paint);
            return layer;
        }

        private static int LeftOf(Core.Documents.Layer l) =>
            Core.Documents.SpriteSheetTools.Bounds(l.Bitmap, new SKRectI(0, 0, l.Bitmap.Width, l.Bitmap.Height))!.Value.Left;

        [Fact]
        public void Align_Distribute_AndArrange() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.StandardImage, 200, 100, null);
            var a = Box(doc, 10, 10).Id;
            var b = Box(doc, 50, 40).Id;
            var c = Box(doc, 150, 70).Id;
            var layers = doc.Layers;
            Core.Documents.Layer L(Guid id) => layers.Scene.Layers.First(l => l.Id == id);
            void Select(params Guid[] ids) => layers.SetSelectedLayers(ids.Select(L));

            Select(a, b, c);
            layers.AlignLayers("Left");
            Assert.Equal(10, LeftOf(L(b)));
            Assert.Equal(10, LeftOf(L(c)));
            layers.History.Undo();                                  // one step
            Assert.Equal(50, LeftOf(L(b)));
            Assert.Equal(150, LeftOf(L(c)));

            layers.DistributeLayers("Horizontal");                  // the selection survives the undo                  // 10, 50, 150 -> 10, 80, 150
            Assert.Equal(80, LeftOf(L(b)));

            layers.ActivateFrameLayer(L(c));
            Select(c);
            layers.AlignLayers("HCenter");                          // alone: to the canvas
            Assert.Equal(95, LeftOf(L(c)));

            layers.ActivateFrameLayer(L(a));
            layers.BringToFrontCommand.Execute(null);
            Assert.Equal(a, layers.Scene.Layers[^1].Id);
            layers.SendToBackCommand.Execute(null);
            Assert.Equal(a, layers.Scene.Layers[0].Id);
        });
    }
}
