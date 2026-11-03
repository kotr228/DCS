using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    /// <summary>Smudge, History Brush, Background Eraser, Magic Eraser and Red Eye,
    /// driven through the canvas the way mouse input is.</summary>
    public class RetouchToolTests
    {
        private sealed class Harness
        {
            public ToolboxViewModel Toolbox { get; } = new();
            public DocumentViewModel Document { get; }
            public CanvasViewModel Canvas => Document.Canvas;
            public Core.Documents.Layer Layer => Document.Layers.ActiveLayer!.Model;

            public Harness(SKColor background)
            {
                Document = new DocumentViewModel(Toolbox, "Test");
                Canvas.SetViewportSize(800, 600);
                Layer.Canvas.Clear(background);
            }

            public void Use(ToolType tool) => Toolbox.SelectTool(tool);

            public T Options<T>() => (T)Toolbox.CurrentToolOptions;

            public SKPoint Device(float x, float y) =>
                new((float)(x * Canvas.Zoom + Canvas.PanX), (float)(y * Canvas.Zoom + Canvas.PanY));

            public void Drag(params (float X, float Y)[] points)
            {
                Canvas.OnPointerPressed(Device(points[0].X, points[0].Y));
                foreach (var p in points.Skip(1)) Canvas.OnPointerMoved(Device(p.X, p.Y));
                Canvas.OnPointerReleased(Device(points[^1].X, points[^1].Y));
            }

            public void Click(float x, float y) => Drag((x, y));

            public SKColor Pixel(int x, int y) => Layer.Bitmap.GetPixel(x, y);
        }

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

        [Fact]
        public void Smudge_DragsColorAlongTheStroke() => Sta(() =>
        {
            var h = new Harness(SKColors.White);
            using (var black = new SKPaint { Color = SKColors.Black })
                h.Layer.Canvas.DrawRect(80, 80, 40, 40, black);

            h.Use(ToolType.Smudge);
            h.Options<PaintToolOptionsViewModel>().Size = 30;
            h.Options<PaintToolOptionsViewModel>().Hardness = 100;
            h.Drag((100, 100), (130, 100), (160, 100), (190, 100));

            Assert.True(h.Pixel(140, 100).Red < 200, $"smudged {h.Pixel(140, 100)}");
            Assert.True(h.Document.Layers.History.CanUndo);
        });

        [Fact]
        public void HistoryBrush_PaintsBackTheOpeningState() => Sta(() =>
        {
            var h = new Harness(new SKColor(200, 30, 30));
            h.Document.Layers.CaptureOpeningState();
            Assert.Equal("Opening state", h.Document.Layers.HistoryBrushSource!.Name);

            h.Layer.Canvas.Clear(SKColors.Transparent);   // everything erased
            h.Use(ToolType.HistoryBrush);
            h.Options<PaintToolOptionsViewModel>().Size = 40;
            h.Options<PaintToolOptionsViewModel>().Hardness = 100;
            h.Drag((100, 100), (160, 100));

            var p = h.Pixel(130, 100);
            Assert.Equal(255, p.Alpha);
            Assert.InRange(p.Red, 195, 205);
            Assert.Equal(0, h.Pixel(300, 300).Alpha);       // untouched area stays erased
        });

        [Fact]
        public void HistoryBrush_WithoutSource_ExplainsWhy() => Sta(() =>
        {
            var h = new Harness(SKColors.White);
            h.Use(ToolType.HistoryBrush);
            h.Drag((100, 100), (160, 100));
            Assert.False(h.Document.Layers.History.CanUndo);
            Assert.Contains("History Brush source", h.Canvas.ToolHintText);
        });

        [Fact]
        public void BackgroundEraser_ErasesOnlyTheColorUnderTheCenter() => Sta(() =>
        {
            var h = new Harness(new SKColor(40, 120, 220));            // blue sky
            using (var red = new SKPaint { Color = new SKColor(220, 40, 40) })
                h.Layer.Canvas.DrawRect(100, 110, 100, 50, red);       // red object below y = 110

            h.Use(ToolType.BackgroundEraser);
            h.Options<PaintToolOptionsViewModel>().Size = 40;
            h.Options<PaintToolOptionsViewModel>().Hardness = 100;
            h.Drag((120, 100), (180, 100));                            // brush overlaps both

            Assert.Equal(0, h.Pixel(150, 100).Alpha);                  // sky erased
            Assert.Equal(255, h.Pixel(150, 115).Alpha);                // object kept
        });

        [Fact]
        public void MagicEraser_ErasesTheConnectedArea() => Sta(() =>
        {
            var h = new Harness(SKColors.White);
            using (var black = new SKPaint { Color = SKColors.Black })
                h.Layer.Canvas.DrawRect(100, 100, 50, 50, black);

            h.Use(ToolType.MagicEraser);
            Assert.False(h.Options<FillToolOptionsViewModel>().ShowColorPicker);
            h.Click(10, 10);

            Assert.Equal(0, h.Pixel(10, 10).Alpha);
            Assert.Equal(0, h.Pixel(400, 300).Alpha);
            Assert.Equal(255, h.Pixel(120, 120).Alpha);
            h.Document.Layers.History.Undo();
            Assert.Equal(255, h.Pixel(10, 10).Alpha);
        });

        [Fact]
        public void RedEye_RemovesRedFromThePupilOnly() => Sta(() =>
        {
            var h = new Harness(new SKColor(230, 190, 170));            // skin
            using (var red = new SKPaint { Color = new SKColor(210, 30, 40), IsAntialias = true })
                h.Layer.Canvas.DrawCircle(200, 200, 12, red);

            h.Use(ToolType.RedEye);
            h.Click(200, 200);

            var pupil = h.Pixel(200, 200);
            Assert.True(pupil.Red < 80, $"pupil {pupil}");
            Assert.Equal(new SKColor(230, 190, 170), h.Pixel(225, 200));   // skin around it untouched

            h.Click(400, 400);                                              // nothing red there
            Assert.Contains("No red", h.Canvas.ToolHintText);
        });
    }
}
