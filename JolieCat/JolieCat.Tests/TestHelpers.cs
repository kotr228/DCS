using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.Tests
{
    internal static class TestHelpers
    {
        public static Layer SolidLayer(string name, int width, int height, SKColor color)
        {
            var layer = new Layer(name, width, height);
            layer.Canvas.Clear(color);
            return layer;
        }

        public static void FillRect(Layer layer, SKRect rect, SKColor color)
        {
            using var paint = new SKPaint { Color = color, BlendMode = SKBlendMode.Src };
            layer.Canvas.DrawRect(rect, paint);
        }

        public static SKBitmap Solid(int width, int height, SKColor color)
        {
            var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(color);
            return bitmap;
        }
    }
}
