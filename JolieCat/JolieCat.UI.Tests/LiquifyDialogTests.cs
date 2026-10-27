using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class LiquifyDialogTests
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

        /// <summary>A white 2000x1000 image with a red bar at x 400..500.</summary>
        private static SKBitmap Image()
        {
            var bitmap = new SKBitmap(2000, 1000, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(400, 0, 100, 1000, red);
            return bitmap;
        }

        [Fact]
        public void Preview_IsScaledDown_ToFitTheDialog() => Sta(() =>
        {
            using var source = Image();
            using var vm = new LiquifyViewModel(source, 900, 640);

            Assert.Equal(900, vm.PreviewWidth);
            Assert.Equal(450, vm.PreviewHeight);
            Assert.Equal(0.45, vm.PreviewScale, 3);
            Assert.Equal(vm.BrushSize * 0.45, vm.BrushPreviewDiameter, 3);
        });

        [Fact]
        public void WarpStroke_MovesTheFullSizeImage_AndUndoRestoresIt() => Sta(() =>
        {
            using var source = Image();
            using var vm = new LiquifyViewModel(source, 900, 640);
            vm.BrushSize = 400;
            vm.Pressure = 100;

            // Drag from the bar (x 450 -> preview 202) 135 preview px to the right.
            vm.BeginStroke(202, 225);
            for (var x = 212; x <= 337; x += 10) vm.ContinueStroke(x, 225);
            vm.EndStroke();
            Assert.True(vm.HasChanges);
            Assert.True(vm.CanUndo);

            using (var result = vm.ApplyTo(source))
            {
                // The bar under the brush travels with it to the end of the stroke (~x 750).
                var moved = result.GetPixel(720, 500);
                Assert.True(moved.Red > 200 && moved.Green < 80, $"{moved}");
                Assert.Equal(SKColors.White, result.GetPixel(450, 500));          // and left its old place
                Assert.Equal(SKColors.Red, result.GetPixel(450, 5));               // the top is outside the brush
            }

            vm.UndoStrokeCommand.Execute(null);
            Assert.False(vm.HasChanges);
            using var undone = vm.ApplyTo(source);
            Assert.Equal(SKColors.White, undone.GetPixel(720, 500));
            Assert.Equal(SKColors.Red, undone.GetPixel(450, 500));
        });

        [Fact]
        public void HoldingStill_KeepsBloating() => Sta(() =>
        {
            using var source = Image();
            using var vm = new LiquifyViewModel(source, 900, 640);
            vm.Tool = LiquifyTool.Bloat;
            vm.BrushSize = 300;

            vm.BeginStroke(202, 225);
            var once = vm.Field.MaxDisplacement;
            for (var i = 0; i < 10; i++) vm.Tick();
            vm.EndStroke();

            Assert.True(vm.Field.MaxDisplacement > once * 5);
        });

        [Fact]
        public void Backdrop_ShowsUnderTheLayer_AndCanBeHidden() => Sta(() =>
        {
            // A layer with one red square on transparency, over a blue backdrop.
            using var layer = new SKBitmap(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
            layer.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(layer))
            using (var red = new SKPaint { Color = SKColors.Red })
                canvas.DrawRect(0, 0, 50, 50, red);
            using var backdrop = new SKBitmap(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
            backdrop.Erase(SKColors.Blue);

            using var vm = new LiquifyViewModel(layer, 900, 640, backdrop);
            Assert.True(vm.HasBackdrop);
            Assert.True(vm.ShowBackdrop);

            uint Pixel(int x, int y)
            {
                var buffer = new uint[1];
                vm.Preview.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), buffer, 4, 0);
                return buffer[0]; // BGRA as 0xAARRGGBB
            }

            Assert.Equal(0xFFFF0000u, Pixel(10, 10));   // the layer
            Assert.Equal(0xFF0000FFu, Pixel(150, 80));  // the backdrop behind it

            vm.ShowBackdrop = false;
            Assert.Equal(0u, Pixel(150, 80) >> 24);     // transparent again
        });

        [Fact]
        public void RestoreAll_IsItselfUndoable() => Sta(() =>
        {
            using var source = Image();
            using var vm = new LiquifyViewModel(source, 900, 640);
            vm.Tool = LiquifyTool.TwirlClockwise;
            vm.BeginStroke(300, 200);
            vm.Tick();
            vm.EndStroke();

            vm.RestoreAllCommand.Execute(null);
            Assert.False(vm.HasChanges);
            vm.UndoStrokeCommand.Execute(null);
            Assert.True(vm.HasChanges);
        });
    }
}
