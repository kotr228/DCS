using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ParagraphTextToolTests
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
        public void DraggingABox_MakesParagraphText_ClickingMakesPointText() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "P", ProjectType.StandardImage, 600, 400, null);
            var canvas = doc.Canvas;
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            toolbox.SelectTool(ToolType.TextHorizontal);

            canvas.OnPointerPressed(Device(50, 50));
            canvas.OnPointerMoved(Device(250, 120));
            Assert.NotNull(canvas.TextBoxDraft);
            canvas.OnPointerReleased(Device(250, 120));
            Assert.True(canvas.IsTextEditing);
            Assert.Equal(200 * canvas.Zoom, canvas.TextEditBoxScreenWidth, 1);
            canvas.TextEditContent = "words that will certainly need more than one line in a box this narrow";
            canvas.CommitTextEdit();
            var text = doc.Layers.Scene.ActiveLayer!.Text!;
            Assert.Equal(200, text.BoxWidth);
            Assert.Equal(new SKPoint(50, 50), text.Origin);
            Assert.True(text.Bounds.Height > text.FontSize * 2);

            canvas.OnPointerPressed(Device(400, 300));       // a plain click elsewhere: point text
            canvas.OnPointerReleased(Device(400, 300));
            Assert.True(canvas.IsTextEditing);
            Assert.Equal(0, canvas.TextEditBoxScreenWidth);
            canvas.TextEditContent = "point";
            canvas.CommitTextEdit();
            Assert.Equal(0, doc.Layers.Scene.ActiveLayer!.Text!.BoxWidth);
        });
    }
}
