using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class TextRevealTests
    {
        [Fact]
        public void TextTypesOut_BetweenKeys()
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.ClipbarAnimation, 400, 100, null);
                    doc.Layers.ActiveLayer!.Model.IsVisible = false;   // no background
                    doc.Layers.AddTextLayer(new TextContent("HELLOWORLD", "Arial", 40, false, false, SKColors.Black, new SKPoint(10, 60), false));
                    var text = doc.Layers.ActiveLayer!.Model;
                    Assert.True(text.IsText);

                    var t = doc.Timeline;
                    t.AnimateActiveLayerCommand.Execute(null);
                    Assert.True(t.IsTextTrack);
                    t.PoseReveal = 0;                                  // nothing at frame 0
                    t.PoseEasing = Core.Animation.KeyEasing.Linear;
                    t.CurrentFrame = 10;
                    t.PoseReveal = 100;                                // all of it at frame 10

                    int Width(int frame)
                    {
                        t.CurrentFrame = frame;
                        using var flat = SceneCompositor.Flatten(doc.Layers.Scene, 400, 100);
                        var bounds = SelectionOps.TrimBounds(flat, null);
                        return bounds.IsEmpty ? 0 : bounds.Width;
                    }
                    var none = Width(0);
                    var half = Width(5);
                    var all = Width(10);
                    Assert.Equal(0, none);
                    Assert.InRange(half, all / 3, all * 2 / 3);         // about half the letters
                    Assert.Null(text.AnimationBitmap);                  // fully shown: the layer's own pixels
                    Assert.Equal("HELLOWORLD", text.Text!.Text);        // the text itself is untouched
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }
    }
}
