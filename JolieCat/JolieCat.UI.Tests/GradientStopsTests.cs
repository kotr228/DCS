using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class GradientStopsTests
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

        private static SKBitmap Render(GradientKind kind, SKColor[] colors, float[] positions)
        {
            var bitmap = new SKBitmap(101, 5, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            using var shader = CanvasViewModel.CreateGradientShader(kind, new SKPoint(0.5f, 2), new SKPoint(100.5f, 2), colors, positions);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(0, 0, 101, 5, paint);
            return bitmap;
        }

        [Fact]
        public void ThreeStops_PassThroughTheMiddleColor()
        {
            using var b = Render(GradientKind.Linear, new[] { SKColors.Red, SKColors.Lime, SKColors.Blue }, new[] { 0f, 0.5f, 1f });
            Assert.True(b.GetPixel(0, 2).Red > 240);
            Assert.True(b.GetPixel(50, 2).Green > 240, $"{b.GetPixel(50, 2)}");
            Assert.True(b.GetPixel(100, 2).Blue > 240);
        }

        [Fact]
        public void Reflected_MirrorsTheStops_AroundTheStart()
        {
            // Start at x=50, end at x=100: red at the start, blue at both ends.
            using var bitmap = new SKBitmap(101, 5, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            using (var shader = CanvasViewModel.CreateGradientShader(GradientKind.Reflected, new SKPoint(50.5f, 2), new SKPoint(100.5f, 2),
                       new[] { SKColors.Red, SKColors.Lime, SKColors.Blue }, new[] { 0f, 0.3f, 1f }))
            using (var paint = new SKPaint { Shader = shader })
                canvas.DrawRect(0, 0, 101, 5, paint);

            Assert.True(bitmap.GetPixel(50, 2).Red > 240);
            Assert.True(bitmap.GetPixel(0, 2).Blue > 230 && bitmap.GetPixel(100, 2).Blue > 230);
            Assert.True(bitmap.GetPixel(35, 2).Green > 200 && bitmap.GetPixel(65, 2).Green > 200);
        }

        [Fact]
        public void Stops_SortReverseAndInterpolate() => Sta(() =>
        {
            var options = new GradientToolOptionsViewModel();
            Assert.Equal(GradientColors.ForegroundToBackground, options.Colors); // presets don't switch it by themselves

            options.LoadPresetCommand.Execute("B&W");
            Assert.True(options.IsCustom);
            options.Stops[1].Location = 0;   // white now first...
            options.Stops[0].Location = 100; // ...black last

            var (colors, positions) = options.StopArrays();
            Assert.Equal(SKColors.White, colors[0]);
            Assert.Equal(new[] { 0f, 1f }, positions);

            var gray = options.ColorAt(0.5);
            Assert.InRange(gray.Red, 126, 129);

            var (rc, rp) = GradientToolOptionsViewModel.Reversed(colors, positions);
            Assert.Equal(SKColors.Black, rc[0]);
            Assert.Equal(new[] { 0f, 1f }, rp);
        });

        [Fact]
        public void AddStop_FillsTheWidestGap_AndAGradientKeepsTwoStops() => Sta(() =>
        {
            var options = new GradientToolOptionsViewModel();
            options.LoadPresetCommand.Execute("B&W");

            options.AddStopCommand.Execute(null);
            Assert.Equal(3, options.Stops.Count);
            Assert.Equal(50, options.Stops[2].Location);
            Assert.Equal("#808080", options.Stops[2].Hex);

            options.RemoveStopCommand.Execute(options.Stops[2]);
            options.RemoveStopCommand.Execute(options.Stops[1]);   // refused: only two left
            Assert.Equal(2, options.Stops.Count);
        });

        [Fact]
        public void FadePreset_EndsTransparent() => Sta(() =>
        {
            var options = new GradientToolOptionsViewModel();
            options.LoadPresetCommand.Execute("Fade");
            var (colors, _) = options.StopArrays();
            Assert.Equal(255, colors[0].Alpha);
            Assert.Equal(0, colors[^1].Alpha);
        });
    }
}
