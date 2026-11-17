using System;
using JolieCat.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace JolieCat.Tests
{
    public class SubjectSelectTests
    {
        private readonly ITestOutputHelper _out;
        public SubjectSelectTests(ITestOutputHelper output) => _out = output;

        /// <summary>A red-orange "fruit" with a dark spot, on a noisy blue-green backdrop.</summary>
        private static SKBitmap Photo(int w, int h, SKRect subject)
        {
            var b = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            var rnd = new Random(5);
            using var c = new SKCanvas(b);
            using (var back = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), new[] { new SKColor(40, 90, 140), new SKColor(60, 150, 120) }, SKShaderTileMode.Clamp) })
                c.DrawRect(0, 0, w, h, back);
            using (var fruit = new SKPaint { Shader = SKShader.CreateRadialGradient(new SKPoint(subject.MidX, subject.MidY), subject.Width / 2, new[] { new SKColor(250, 150, 60), new SKColor(200, 60, 30) }, SKShaderTileMode.Clamp), IsAntialias = true })
                c.DrawOval(subject, fruit);
            using (var spot = new SKPaint { Color = new SKColor(90, 40, 20) })
                c.DrawCircle(subject.MidX + subject.Width / 6, subject.MidY, subject.Width / 12, spot);
            for (var i = 0; i < w * h / 20; i++)
            {
                int x = rnd.Next(w), y = rnd.Next(h);
                var p = b.GetPixel(x, y);
                var d = rnd.Next(-20, 21);
                b.SetPixel(x, y, new SKColor((byte)Math.Clamp(p.Red + d, 0, 255), (byte)Math.Clamp(p.Green + d, 0, 255), (byte)Math.Clamp(p.Blue + d, 0, 255)));
            }
            return b;
        }

        [Theory]
        [InlineData(400, 300, 120, 70, 300, 240)]     // in the middle
        [InlineData(600, 400, 260, 60, 520, 330)]     // off to one side
        public void FindsTheSubject(int w, int h, float l, float t, float r, float bottom)
        {
            var subject = new SKRect(l, t, r, bottom);
            using var photo = Photo(w, h, subject);
            var mask = SubjectSelect.Find(photo);
            _out.WriteLine($"certainty {SubjectSelect.LastCertainty:0.000} share {SubjectSelect.LastShare:0.000}");
            Assert.NotNull(mask);
            int both = 0, either = 0;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f - subject.MidX) / (subject.Width / 2), dy = (y + 0.5f - subject.MidY) / (subject.Height / 2);
                    var truth = dx * dx + dy * dy <= 1;
                    var got = mask![y * w + x] >= 128;
                    if (truth && got) both++;
                    if (truth || got) either++;
                }
            var overlap = both / (double)either;
            _out.WriteLine($"overlap {overlap:0.000}");
            Assert.True(overlap > 0.9, $"overlap {overlap:0.000}");
        }

        [Fact]
        public void PlainPicture_HasNoSubject()
        {
            using var plain = new SKBitmap(100, 80, SKColorType.Rgba8888, SKAlphaType.Premul);
            plain.Erase(new SKColor(120, 120, 120));
            var found = SubjectSelect.Find(plain);
            _out.WriteLine($"plain: certainty {SubjectSelect.LastCertainty:0.000} share {SubjectSelect.LastShare:0.000}");
            Assert.Null(found);
        }
    }
}
