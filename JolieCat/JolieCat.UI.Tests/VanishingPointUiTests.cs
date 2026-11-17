using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class VanishingPointUiTests
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
        public void Dialog_PlacesThePicture_AndAddsALayer() => Sta(() =>
        {
            using var photo = new SKBitmap(400, 300, SKColorType.Rgba8888, SKAlphaType.Premul);
            photo.Erase(SKColors.Gray);
            var sources = new List<(string, Func<SKBitmap?>)>
            {
                ("Red", () => { var b = new SKBitmap(10, 10, SKColorType.Rgba8888, SKAlphaType.Premul); b.Erase(SKColors.Red); return b; }),
                ("Nothing", () => null),
            };
            using var model = new VanishingPointViewModel(photo, sources);
            Assert.True(model.HasPicture);
            Assert.NotNull(model.Preview);
            Assert.Contains("M", model.GridPath);

            // The surface: the left half of the picture; tiled, it covers all of it.
            model.MoveCorner(0, 0, 0);
            model.MoveCorner(1, 200 * model.Scale, 0);
            model.MoveCorner(2, 200 * model.Scale, 300 * model.Scale);
            model.MoveCorner(3, 0, 300 * model.Scale);
            model.Tile = true;
            using (var layer = model.RenderLayer()!)
            {
                Assert.Equal(400, layer.Width);
                Assert.Equal(SKColors.Red, layer.GetPixel(100, 150));
                Assert.Equal(0, layer.GetPixel(300, 150).Alpha);
            }

            model.Source = "Nothing";
            Assert.False(model.HasPicture);
            Assert.Null(model.RenderLayer());

            var doc = new DocumentViewModel(new ToolboxViewModel(), "V", ProjectType.StandardImage, 40, 30, null);
            using var picture = new SKBitmap(40, 30, SKColorType.Rgba8888, SKAlphaType.Premul);
            picture.Erase(SKColors.Blue);
            doc.Layers.AddPictureLayer("Vanishing Point", picture);
            Assert.Equal("Vanishing Point", doc.Layers.Scene.ActiveLayer!.Name);
            Assert.Equal(SKColors.Blue, doc.Layers.Scene.ActiveLayer.Bitmap.GetPixel(5, 5));
            doc.Layers.History.Undo();
            Assert.Single(doc.Layers.Scene.Layers);
        });
    }
}
