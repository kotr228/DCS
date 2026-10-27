using JolieCat.Core.Documents;
using JolieCat.Core.Formats;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class PsdCodecTests
    {
        [Fact]
        public void RoundTrip_PreservesLayersAndProperties()
        {
            using var scene = new Scene("Doc");
            var background = scene.AddLayer("Background", 40, 30);
            background.Canvas.Clear(SKColors.White);

            var top = scene.AddLayer("Шар з назвою", 40, 30); // non-ASCII name survives via 'luni'
            TestHelpers.FillRect(top, new SKRect(5, 6, 20, 18), new SKColor(200, 50, 25));
            top.Opacity = 0.5;
            top.BlendMode = BlendMode.Multiply;
            top.IsVisible = false;

            using var stream = new MemoryStream();
            PsdCodec.Write(stream, scene, 40, 30);
            stream.Position = 0;

            var psd = PsdCodec.Read(stream);
            using var loaded = psd.Scene;

            Assert.Equal(40, psd.Width);
            Assert.Equal(30, psd.Height);
            Assert.Equal(2, loaded.Layers.Count);

            var loadedTop = loaded.Layers[1];
            Assert.Equal("Шар з назвою", loadedTop.Name);
            Assert.Equal(BlendMode.Multiply, loadedTop.BlendMode);
            Assert.False(loadedTop.IsVisible);
            Assert.Equal(0.5, loadedTop.Opacity, 2);

            Assert.Equal(new SKColor(200, 50, 25), loadedTop.Bitmap.GetPixel(10, 10));
            Assert.Equal(0, loadedTop.Bitmap.GetPixel(30, 25).Alpha); // outside the painted rect
            Assert.Equal(SKColors.White, loaded.Layers[0].Bitmap.GetPixel(0, 0));
        }

        [Fact]
        public void RoundTrip_PreservesMask()
        {
            using var scene = new Scene("Doc");
            var layer = scene.AddLayer("Masked", 16, 16);
            layer.Canvas.Clear(SKColors.Red);
            var mask = layer.AddMask();
            using (var black = new SKPaint { Color = SKColors.Black })
                mask.Canvas.DrawRect(0, 0, 8, 16, black);
            mask.IsEnabled = false;

            using var stream = new MemoryStream();
            PsdCodec.Write(stream, scene, 16, 16);
            stream.Position = 0;
            using var loaded = PsdCodec.Read(stream).Scene;

            var loadedMask = loaded.Layers[0].Mask;
            Assert.NotNull(loadedMask);
            Assert.False(loadedMask!.IsEnabled);
            Assert.Equal(0, loadedMask.Bitmap.GetPixel(2, 2).Red);
            Assert.Equal(255, loadedMask.Bitmap.GetPixel(12, 2).Red);
        }

        [Fact]
        public void EmptyLayer_RoundTrips()
        {
            using var scene = new Scene("Doc");
            scene.AddLayer("Empty", 10, 10);

            using var stream = new MemoryStream();
            PsdCodec.Write(stream, scene, 10, 10);
            stream.Position = 0;
            using var loaded = PsdCodec.Read(stream).Scene;

            Assert.Single(loaded.Layers);
            Assert.Equal(0, loaded.Layers[0].Bitmap.GetPixel(5, 5).Alpha);
        }

        [Fact]
        public void Read_RejectsNonPsd()
        {
            using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            Assert.Throws<InvalidDataException>(() => PsdCodec.Read(stream));
        }

        [Theory]
        [InlineData(new byte[] { })]
        [InlineData(new byte[] { 7 })]
        [InlineData(new byte[] { 1, 1, 1, 1, 1, 2, 3, 4, 4, 4, 9 })]
        public void PackBits_RoundTrips(byte[] data)
        {
            var encoded = PsdCodec.PackBits.Encode(data, 0, data.Length);
            var decoded = new byte[data.Length];
            PsdCodec.PackBits.Decode(encoded, decoded, 0, data.Length);
            Assert.Equal(data, decoded);
        }

        [Fact]
        public void PackBits_RoundTripsLongRuns()
        {
            var data = new byte[1000];
            for (var i = 0; i < data.Length; i++) data[i] = (byte)(i < 400 ? 5 : i % 7);

            var encoded = PsdCodec.PackBits.Encode(data, 0, data.Length);
            var decoded = new byte[data.Length];
            PsdCodec.PackBits.Decode(encoded, decoded, 0, data.Length);

            Assert.Equal(data, decoded);
            Assert.True(encoded.Length < data.Length);
        }
    }
}
