using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Tests
{
    public class ColorPickerDialogTests
    {
        [Fact]
        public void StartsFromTheGivenColor()
        {
            var vm = new ColorPickerDialogViewModel("#FF8000");
            Assert.Equal((255, 128, 0), (vm.Red, vm.Green, vm.Blue));
            Assert.Equal(30, vm.Hue, 0);
            Assert.Equal("#FF8000", vm.Hex);
        }

        [Fact]
        public void EveryFieldKeepsTheOthersInStep()
        {
            var vm = new ColorPickerDialogViewModel("#000000");

            vm.Hex = "#00FF00";
            Assert.Equal((0, 255, 0), (vm.Red, vm.Green, vm.Blue));
            Assert.Equal(120, vm.Hue, 0);

            vm.Blue = 255;
            Assert.Equal("#00FFFF", vm.Hex);

            vm.Hue = 0; // red at full saturation and brightness
            Assert.Equal("#FF0000", vm.Hex);
            Assert.Equal(System.Windows.Media.Color.FromRgb(255, 0, 0), vm.NewBrush.Color);
        }

        [Fact]
        public void HalfTypedHex_IsIgnored_AndGrayKeepsTheHue()
        {
            var vm = new ColorPickerDialogViewModel("#3366CC");
            var hue = vm.Hue;

            vm.Hex = "#12";            // still typing
            Assert.Equal((51, 102, 204), (vm.Red, vm.Green, vm.Blue));

            vm.Hex = "#808080";        // gray: hue is undefined, keep the old one
            Assert.Equal(hue, vm.Hue, 3);
            Assert.Equal(0, vm.Saturation, 3);
        }
    }
}
