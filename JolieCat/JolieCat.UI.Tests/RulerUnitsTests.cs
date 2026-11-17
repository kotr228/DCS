using System.IO;
using JolieCat.UI.Media;
using JolieCat.UI.Rendering;

namespace JolieCat.UI.Tests
{
    public class RulerUnitsTests
    {
        [Fact]
        public void Units_ConvertAndStepSensibly()
        {
            Assert.Equal(1, CanvasRenderer.PixelsPerUnit(CanvasRenderer.RulerUnit.Pixels, 300, 1000));
            Assert.Equal(300, CanvasRenderer.PixelsPerUnit(CanvasRenderer.RulerUnit.Inches, 300, 1000));
            Assert.Equal(300 / 2.54, CanvasRenderer.PixelsPerUnit(CanvasRenderer.RulerUnit.Centimeters, 300, 1000), 6);
            Assert.Equal(10, CanvasRenderer.PixelsPerUnit(CanvasRenderer.RulerUnit.Percent, 300, 1000));

            // At 100% zoom and 72 ppi an inch is 72 screen pixels: one-inch steps.
            Assert.Equal(1, CanvasRenderer.UnitStep(72, wholeOnly: false));
            // Zoomed in 8x on 72 ppi: 576 px per inch, steps of 0.2 inch.
            Assert.Equal(0.2, CanvasRenderer.UnitStep(576, wholeOnly: false), 6);
            // Pixels never get steps below 1.
            Assert.Equal(1, CanvasRenderer.UnitStep(3200, wholeOnly: true));
            // Zoomed out: big round steps.
            Assert.Equal(500, CanvasRenderer.UnitStep(0.15, wholeOnly: true));
        }

        [Fact]
        public void Preferences_KeepTheNewSettings_InRange()
        {
            var file = Path.Combine(Path.GetTempPath(), $"prefs-{Guid.NewGuid():N}.json");
            var old = Preferences.FilePath;
            Preferences.FilePath = file;
            try
            {
                new Preferences { RulerUnits = "Centimeters", Resolution = 300, GridSize = "Large", GridColors = "Light" }.Clamped().Save();
                var loaded = Preferences.Load();
                Assert.Equal("Centimeters", loaded.RulerUnits);
                Assert.Equal(300, loaded.Resolution);
                Assert.Equal("Large", loaded.GridSize);
                Assert.Equal("Light", loaded.GridColors);
                var odd = new Preferences { RulerUnits = "Furlongs", GridSize = "Huge", GridColors = "Pink", Resolution = 0 }.Clamped();
                Assert.Equal("Pixels", odd.RulerUnits);
                Assert.Equal("Medium", odd.GridSize);
                Assert.Equal("Dark", odd.GridColors);
                Assert.Equal(1, odd.Resolution);

                CanvasRenderer.SetTransparencyGrid("Small", "Light");      // rebuilds without trouble
                CanvasRenderer.SetTransparencyGrid("Medium", "Dark");
            }
            finally
            {
                Preferences.FilePath = old;
                File.Delete(file);
            }
        }
    }
}
