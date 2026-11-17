using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class EveryFilterTests
    {
        /// <summary>Every Filter menu entry has a dialog setup and a way to apply it.</summary>
        [Fact]
        public void EveryFilterKind_OpensAndApplies()
        {
            using var picture = new SKBitmap(32, 24, SKColorType.Rgba8888, SKAlphaType.Premul);
            picture.Erase(new SKColor(200, 120, 40));
            foreach (var kind in Enum.GetValues<FilterKind>())
            {
                var options = new FilterOptionsViewModel(kind);
                Assert.False(string.IsNullOrEmpty(options.Title), kind.ToString());
                using var result = options.Apply(picture);
                Assert.Equal(32, result.Width);
                Assert.Equal(24, result.Height);
            }
        }
    }
}
