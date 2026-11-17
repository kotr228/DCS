using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class PathsPanelTests
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
        public void PathText_KeepsAnchorsAndHandles()
        {
            var path = new VectorPath { IsClosed = true };
            path.Anchors.Add(new PathAnchor(new SKPoint(10, 20)) { OutHandle = new SKPoint(30, 5) });
            path.Anchors.Add(new PathAnchor(new SKPoint(80.5f, 40)) { InHandle = new SKPoint(60, 60), Type = AnchorPointType.Smooth });
            path.Anchors.Add(new PathAnchor(new SKPoint(20, 90)));
            var back = VectorPath.FromText(path.ToText())!;
            Assert.True(back.IsClosed);
            Assert.Equal(3, back.Anchors.Count);
            Assert.Equal(new SKPoint(30, 5), back.Anchors[0].OutHandle);
            Assert.Null(back.Anchors[0].InHandle);
            Assert.Equal(new SKPoint(80.5f, 40), back.Anchors[1].Position);
            Assert.Equal(AnchorPointType.Smooth, back.Anchors[1].Type);
            Assert.Null(VectorPath.FromText("nonsense"));
        }

        [Fact]
        public void SaveLoadAndDelete_ThroughThePanel() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "P", ProjectType.StandardImage, 300, 200, null);
            var canvas = doc.Canvas;
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            toolbox.SelectTool(ToolType.Pen);
            foreach (var (x, y) in new[] { (20f, 20f), (200f, 40f), (120f, 150f) })
            {
                canvas.OnPointerPressed(Device(x, y));
                canvas.OnPointerReleased(Device(x, y));
            }
            var panels = new DocumentPanelsViewModel(() => doc, startTimer: false) { ShowPaths = true };
            panels.SaveCurrentPathCommand.Execute(null);
            Assert.Equal("Path 1", Assert.Single(panels.Paths).Name);
            Assert.NotNull(panels.Paths[0].Thumbnail);

            canvas.CancelPenPath();
            Assert.Null(canvas.PenPath);
            panels.LoadPathCommand.Execute(panels.Paths[0]);
            Assert.Equal(3, canvas.PenPath!.Anchors.Count);
            Assert.Equal(new SKPoint(200, 40), canvas.PenPath.Anchors[1].Position);

            panels.DeletePathCommand.Execute(panels.Paths[0]);
            Assert.Empty(panels.Paths);
            Assert.Empty(doc.Layers.Scene.SavedPaths);
        });
    }
}
