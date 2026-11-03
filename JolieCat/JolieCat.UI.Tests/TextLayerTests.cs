using System.IO;
using JolieCat.Core.Documents;
using JolieCat.Core.Serialization;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    /// <summary>Editable text layers: created by the Text tool, re-opened by clicking
    /// the text, moved without losing editability, rasterized before painting.</summary>
    public class TextLayerTests
    {
        [Fact]
        public void ChangingTextSettings_RestylesTheSelectedTextLayer_AsOneUndoStep()
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var toolbox = new ToolboxViewModel();
                    var doc = new DocumentViewModel(toolbox, "T");
                    toolbox.SelectTool(JolieCat.Shared.Enums.ToolType.TextHorizontal);
                    var options = (JolieCat.UI.ViewModels.ToolOptions.TextToolOptionsViewModel)toolbox.CurrentToolOptions;
                    doc.Layers.AddTextLayer(new JolieCat.Core.Documents.TextContent("Hi", "Arial", 30, false, false,
                        SkiaSharp.SKColors.Black, new SkiaSharp.SKPoint(10, 10), false));
                    var layer = doc.Layers.ActiveLayer!.Model;
                    var steps = doc.Layers.History.UndoCount;

                    options.Warp = 40;            // a quick run of changes...
                    options.Warp = 60;
                    options.IsUnderline = true;
                    Assert.Equal(60, layer.Text!.Warp);
                    Assert.True(layer.Text.Underline);

                    doc.Canvas.FlushTextRestyle(); // ...settles into one step
                    Assert.Equal(steps + 1, doc.Layers.History.UndoCount);
                    doc.Layers.History.Undo();
                    Assert.Equal(0, layer.Text!.Warp);
                    Assert.False(layer.Text.Underline);
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
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

        private sealed class Harness
        {
            public ToolboxViewModel Toolbox { get; } = new();
            public DocumentViewModel Document { get; }
            public CanvasViewModel Canvas => Document.Canvas;
            public ViewModels.Layers.LayersViewModel Layers => Document.Layers;

            public Harness()
            {
                Document = new DocumentViewModel(Toolbox, "Test");
                Canvas.SetViewportSize(800, 600);
                Layers.ActiveLayer!.Model.Canvas.Clear(SKColors.White);
            }

            public SKPoint Device(float x, float y) => new((float)(x * Canvas.Zoom + Canvas.PanX), (float)(y * Canvas.Zoom + Canvas.PanY));

            public void Click(float x, float y)
            {
                Canvas.OnPointerPressed(Device(x, y));
                Canvas.OnPointerReleased(Device(x, y));
            }

            public void Drag(float x0, float y0, float x1, float y1)
            {
                Canvas.OnPointerPressed(Device(x0, y0));
                Canvas.OnPointerMoved(Device(x1, y1));
                Canvas.OnPointerReleased(Device(x1, y1));
            }

            /// <summary>Types a word with the Text tool at (x, y) and commits it.</summary>
            public Layer Type(string text, float x, float y)
            {
                Toolbox.SelectTool(ToolType.TextHorizontal);
                if (Toolbox.CurrentToolOptions is TextToolOptionsViewModel options) options.FontSize = 60;
                Canvas.PrimaryColor = System.Windows.Media.Colors.Black;
                Click(x, y);
                Canvas.TextEditContent = text;
                Canvas.CommitTextEdit();
                return Layers.ActiveLayer!.Model;
            }

            public static bool HasInk(Layer layer, SKRect area)
            {
                for (var y = (int)area.Top; y < area.Bottom; y++)
                    for (var x = (int)area.Left; x < area.Right; x++)
                        if (layer.Bitmap.GetPixel(x, y).Alpha > 100) return true;
                return false;
            }
        }

        [Fact]
        public void TextTool_MakesAnEditableTextLayer() => Sta(() =>
        {
            var h = new Harness();
            var background = h.Layers.ActiveLayer!.Model;

            var layer = h.Type("Hello", 200, 200);

            Assert.True(layer.IsText);
            Assert.Equal("Hello", layer.Name);
            Assert.Equal("Hello", layer.Text!.Text);
            Assert.True(Harness.HasInk(layer, new SKRect(200, 200, 340, 260)));
            Assert.Equal(SKColors.White, background.Bitmap.GetPixel(220, 230)); // background untouched
            Assert.Equal("Type Layer", h.Layers.History.UndoNames[^1]);
        });

        [Fact]
        public void ClickingTheText_ReopensIt_AndTheEditIsUndoable() => Sta(() =>
        {
            var h = new Harness();
            var layer = h.Type("Hello", 200, 200);

            h.Click(230, 230); // on the text
            Assert.True(h.Canvas.IsTextEditing);
            Assert.Equal("Hello", h.Canvas.TextEditContent);

            h.Canvas.TextEditContent = "Bye";
            h.Canvas.CommitTextEdit();

            Assert.Equal("Bye", layer.Text!.Text);
            Assert.Equal(2, h.Layers.Scene.Layers.Count); // edited in place, no new layer
            Assert.Equal("Edit Text", h.Layers.History.UndoNames[^1]);

            h.Layers.History.Undo();
            Assert.Equal("Hello", h.Layers.Scene.Layers[1].Text!.Text);
        });

        [Fact]
        public void CancellingAnEdit_RestoresTheText() => Sta(() =>
        {
            var h = new Harness();
            var layer = h.Type("Hello", 200, 200);

            h.Click(230, 230);
            Assert.False(Harness.HasInk(layer, new SKRect(200, 200, 340, 260))); // hidden while editing
            h.Canvas.CancelTextEdit();

            Assert.True(Harness.HasInk(layer, new SKRect(200, 200, 340, 260)));
            Assert.Equal("Hello", layer.Text!.Text);
        });

        [Fact]
        public void MovingText_KeepsItEditable() => Sta(() =>
        {
            var h = new Harness();
            var layer = h.Type("Hello", 200, 200);

            h.Toolbox.SelectTool(ToolType.Move);
            h.Drag(500, 500, 600, 550);

            Assert.True(layer.IsText);
            Assert.Equal(300, layer.Text!.Origin.X, 1);
            Assert.Equal(250, layer.Text!.Origin.Y, 1);
            Assert.True(Harness.HasInk(layer, new SKRect(300, 250, 440, 310)));

            h.Layers.History.Undo();
            Assert.Equal(200, h.Layers.Scene.Layers[1].Text!.Origin.X, 1);
        });

        [Fact]
        public void PaintingOnText_RasterizesItFirst_Undoably() => Sta(() =>
        {
            var h = new Harness();
            var layer = h.Type("Hello", 200, 200);

            h.Toolbox.SelectTool(ToolType.Brush);
            h.Drag(100, 400, 300, 400);

            Assert.False(layer.IsText);
            Assert.Contains("Rasterize Type", h.Layers.History.UndoNames);

            h.Layers.History.Undo(); // the stroke
            h.Layers.History.Undo(); // the rasterize
            Assert.True(h.Layers.Scene.Layers[1].IsText);
        });

        [Fact]
        public void TextLayers_SurviveProjectSave() => Sta(() =>
        {
            var h = new Harness();
            h.Type("Saved", 120, 80);

            var folder = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "t.jolie");
                ProjectSerializer.Save(path, h.Layers.Scene, Array.Empty<TimelineTrackData>(), 0, 0);
                var loaded = ProjectSerializer.Load(path).Scene;
                using (loaded)
                {
                    var text = loaded.Layers[1].Text;
                    Assert.NotNull(text);
                    Assert.Equal("Saved", text!.Text);
                    Assert.Equal(60, text.FontSize);
                    Assert.Equal(120, text.Origin.X, 1);
                    Assert.Equal(80, text.Origin.Y, 1);
                    Assert.Equal(SKColors.Black, text.Color);
                }
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        });
    }
}
