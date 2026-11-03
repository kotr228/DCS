using JolieCat.Shared.Enums;
using JolieCat.UI.Media;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class BrushDynamicsTests
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

        private static (DocumentViewModel Doc, PaintToolOptionsViewModel Options, Core.Documents.Layer Layer) Brush()
        {
            BrushLibrary.UseForTests(new BrushLibrary(path: null));
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "T");
            doc.Canvas.Zoom = 1;
            doc.Canvas.PanX = 0;
            doc.Canvas.PanY = 0;
            doc.Layers.AddLayerCommand.Execute(null);   // a transparent layer to paint on
            toolbox.SelectTool(ToolType.Brush);
            doc.Canvas.ResetColors();                   // black
            var options = (PaintToolOptionsViewModel)toolbox.CurrentToolOptions;
            options.Hardness = 100;
            options.PressureAffectsSize = false;
            return (doc, options, doc.Layers.ActiveLayer!.Model);
        }

        private static void Dab(DocumentViewModel doc, float x, float y)
        {
            doc.Canvas.OnPointerPressed(new SKPoint(x, y));
            doc.Canvas.OnPointerReleased(new SKPoint(x, y));
        }

        [Fact]
        public void Roundness_FlattensTheTip_AndAngleTurnsIt() => Sta(() =>
        {
            var (doc, options, layer) = Brush();
            options.Size = 40;
            options.Roundness = 20;                  // 40 wide, 8 tall

            Dab(doc, 100, 100);
            Assert.True(layer.Bitmap.GetPixel(115, 100).Alpha > 200);
            Assert.Equal(0, layer.Bitmap.GetPixel(100, 110).Alpha);

            options.Angle = 90;                      // now 8 wide, 40 tall
            Dab(doc, 300, 100);
            Assert.True(layer.Bitmap.GetPixel(300, 115).Alpha > 200);
            Assert.Equal(0, layer.Bitmap.GetPixel(310, 100).Alpha);
        });

        [Fact]
        public void Flow_LaysDownLessPaintPerDab() => Sta(() =>
        {
            var (doc, options, layer) = Brush();
            options.Size = 30;
            options.Flow = 50;
            Dab(doc, 100, 100);
            Assert.InRange(layer.Bitmap.GetPixel(100, 100).Alpha, 120, 135);
        });

        [Fact]
        public void Scatter_SpreadsDabsAwayFromTheStroke() => Sta(() =>
        {
            var (doc, options, layer) = Brush();
            options.Size = 4;
            options.Spacing = 50;
            options.Scatter = 500;                   // up to 40 px off the line

            doc.Canvas.OnPointerPressed(new SKPoint(100, 200));
            doc.Canvas.OnPointerMoved(new SKPoint(500, 200));
            doc.Canvas.OnPointerReleased(new SKPoint(500, 200));

            var offLine = 0;
            for (var x = 100; x < 500; x++)
                for (var y = 170; y < 230; y++)
                    if (Math.Abs(y - 200) > 8 && layer.Bitmap.GetPixel(x, y).Alpha > 0) { offLine++; break; }
            Assert.True(offLine > 20, $"{offLine} columns with paint off the line");
        });

        [Fact]
        public void Presets_LoadTheirSettings_AndYourOwnAreKept() => Sta(() =>
        {
            var (_, options, _) = Brush();
            options.LoadPresetCommand.Execute("Calligraphy");
            Assert.Equal(45, options.Angle);
            Assert.Equal(20, options.Roundness);

            options.Scatter = 77;
            options.SavePresetCommand.Execute(null);
            Assert.Contains("My brush 1", options.Presets);

            var other = new PaintToolOptionsViewModel();
            other.LoadPresetCommand.Execute("My brush 1");
            Assert.Equal(77, other.Scatter);

            other.DeletePresetCommand.Execute("Hard Round");   // built in: stays
            other.DeletePresetCommand.Execute("My brush 1");
            Assert.Contains("Hard Round", other.Presets);
            Assert.DoesNotContain("My brush 1", other.Presets);
        });
    }
}
