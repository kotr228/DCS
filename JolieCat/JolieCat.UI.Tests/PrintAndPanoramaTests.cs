using System.Windows;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class PrintAndPanoramaTests
    {
        [Fact]
        public void PrintSize_FitsThePage_OrUsesTheResolution()
        {
            var page = new Size(768, 1056);                       // 8 x 11 inch printable area
            var fit = MainViewModel.PrintSize(3000, 2000, page, null);
            Assert.Equal(768, fit.Width, 3);                     // the wide image fills the width
            Assert.Equal(512, fit.Height, 3);

            var at300 = MainViewModel.PrintSize(600, 300, page, 300);
            Assert.Equal(192, at300.Width, 3);                   // 2 inches
            var tooBig = MainViewModel.PrintSize(6000, 3000, page, 72);
            Assert.True(tooBig.Width <= 768.001);                // never past the page
        }

        [Fact]
        public void Panorama_OpensAsANewDocument()
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var main = new MainViewModel();
                    using var a = new SKBitmap(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using var b = new SKBitmap(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
                    var random = new Random(5);
                    using var wide = new SKBitmap(320, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
                    for (var y = 0; y < 100; y++)
                        for (var x = 0; x < 320; x++)
                            wide.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
                    using (var c = new SKCanvas(a)) c.DrawBitmap(wide, 0, 0);
                    using (var c = new SKCanvas(b)) c.DrawBitmap(wide, -120, 0);

                    var doc = main.OpenPanorama(new[] { a, b }, "pano");
                    Assert.Same(doc, main.ActiveDocument);
                    Assert.Equal(320, doc.Layers.DocumentWidth);
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
