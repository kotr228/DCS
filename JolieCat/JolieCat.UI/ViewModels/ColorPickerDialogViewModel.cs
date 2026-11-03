using System;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.UI.Media;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// The Color Picker dialog (Photoshop's): a saturation/brightness square and hue
    /// strip, R/G/B and hex fields, and "new" vs "current" swatches. All fields stay in
    /// step; <see cref="Hex"/> is the result.
    /// </summary>
    public partial class ColorPickerDialogViewModel : ObservableObject
    {
        private bool _syncing;

        public ColorPickerDialogViewModel(string? initialHex)
        {
            var color = Parse(initialHex) ?? Colors.Black;
            OriginalBrush = Freeze(new SolidColorBrush(color));
            SetFromRgb(color.R, color.G, color.B, fromHsv: false);
        }

        public SolidColorBrush OriginalBrush { get; }

        [ObservableProperty] private double hue;
        [ObservableProperty] private double saturation;
        [ObservableProperty] private double brightness;
        [ObservableProperty] private int red;
        [ObservableProperty] private int green;
        [ObservableProperty] private int blue;
        [ObservableProperty] private string hex = "#000000";
        [ObservableProperty] private SolidColorBrush newBrush = new(Colors.Black);

        public Color Color => Color.FromRgb((byte)Red, (byte)Green, (byte)Blue);

        partial void OnHueChanged(double value) => FromHsv();
        partial void OnSaturationChanged(double value) => FromHsv();
        partial void OnBrightnessChanged(double value) => FromHsv();
        partial void OnRedChanged(int value) => FromRgbFields();
        partial void OnGreenChanged(int value) => FromRgbFields();
        partial void OnBlueChanged(int value) => FromRgbFields();

        partial void OnHexChanged(string value)
        {
            if (_syncing || Parse(value) is not { } c) return;
            SetFromRgb(c.R, c.G, c.B, fromHsv: false);
        }

        private void FromHsv()
        {
            if (_syncing) return;
            var (r, g, b) = HsvColor.ToRgb(Hue, Saturation, Brightness);
            SetFromRgb(r, g, b, fromHsv: true);
        }

        private void FromRgbFields()
        {
            if (_syncing) return;
            SetFromRgb((byte)Math.Clamp(Red, 0, 255), (byte)Math.Clamp(Green, 0, 255), (byte)Math.Clamp(Blue, 0, 255), fromHsv: false);
        }

        private void SetFromRgb(byte r, byte g, byte b, bool fromHsv)
        {
            _syncing = true;
            Red = r;
            Green = g;
            Blue = b;
            if (!fromHsv)
            {
                // Keep the hue when the color is gray (its hue is undefined).
                var (h, s, v) = HsvColor.FromRgb(r, g, b);
                if (s > 0 && v > 0) Hue = h;
                if (v > 0) Saturation = s;
                Brightness = v;
            }
            Hex = $"#{r:X2}{g:X2}{b:X2}";
            NewBrush = Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));
            _syncing = false;
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        /// <summary>"#RRGGBB", "RRGGBB" or "#RGB"; null if it isn't a color yet.</summary>
        public static Color? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim().TrimStart('#');
            if (t.Length == 3) t = string.Concat(t[0], t[0], t[1], t[1], t[2], t[2]);
            if (t.Length != 6 || !uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return null;
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
    }
}
