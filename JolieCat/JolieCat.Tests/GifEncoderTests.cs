using JolieCat.Core.Serialization;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class GifEncoderTests
    {
        [Fact]
        public void AnimatedGif_DecodesBackToTheSameFrames()
        {
            // Frame 1: noise (fills the code table several times); frame 2: flat + transparency.
            var random = new Random(9);
            using var noise = new SKBitmap(300, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
            var levels = new byte[] { 0, 60, 120, 180, 240 };
            for (var y = 0; y < 200; y++)
                for (var x = 0; x < 300; x++)
                    noise.SetPixel(x, y, new SKColor(levels[random.Next(5)], levels[random.Next(5)], levels[random.Next(3)]));
            using var flat = new SKBitmap(300, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
            flat.Erase(new SKColor(200, 30, 60));
            using (var canvas = new SKCanvas(flat))
            using (var clear = new SKPaint { BlendMode = SKBlendMode.Clear })
                canvas.DrawRect(0, 0, 100, 200, clear);

            using var stream = new MemoryStream();
            GifEncoder.Write(stream, new[] { noise, flat }, new[] { 100, 250 });
            stream.Position = 0;

            using var codec = SKCodec.Create(stream);
            Assert.NotNull(codec);
            Assert.Equal(2, codec.FrameCount);
            Assert.Equal(100, codec.FrameInfo[0].Duration);
            Assert.Equal(250, codec.FrameInfo[1].Duration);
            Assert.Equal(-1, codec.RepetitionCount); // forever

            var info = new SKImageInfo(300, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var first = new SKBitmap(info);
            Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, first.GetPixels(), new SKCodecOptions(0)));
            var mismatches = 0;
            for (var y = 0; y < 200; y += 3)
                for (var x = 0; x < 300; x += 3)
                {
                    var a = noise.GetPixel(x, y); var b = first.GetPixel(x, y);
                    if (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 30) mismatches++;
                }
            Assert.True(mismatches < 20, $"{mismatches} pixels differ");

            using var second = new SKBitmap(info);
            Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, second.GetPixels(), new SKCodecOptions(1)));
            Assert.Equal(0, second.GetPixel(50, 100).Alpha);
            var red = second.GetPixel(200, 100);
            Assert.InRange(red.Red, 195, 205);
        }
    }
}
