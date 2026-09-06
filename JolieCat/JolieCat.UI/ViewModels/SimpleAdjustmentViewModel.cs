using System;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Adjustments;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public enum SimpleAdjustmentKind
    {
        BrightnessContrast,
        HueSaturation,
    }

    /// <summary>
    /// Backs <see cref="Views.SimpleAdjustmentDialog"/> - the shared shell for
    /// Brightness/Contrast (two sliders) and Hue/Saturation/Lightness (three) -
    /// both just a handful of independent sliders, unlike Levels (needs its own
    /// histogram) or Curves (needs its own spline editor).
    /// </summary>
    public partial class SimpleAdjustmentViewModel : ObservableObject
    {
        public SimpleAdjustmentKind Kind { get; }

        public string Title { get; }

        public string Slider1Label { get; }

        public double Slider1Min { get; }

        public double Slider1Max { get; }

        [ObservableProperty]
        private double slider1Value;

        public string Slider2Label { get; }

        public double Slider2Min { get; }

        public double Slider2Max { get; }

        [ObservableProperty]
        private double slider2Value;

        /// <summary>False for Brightness/Contrast (only two sliders) - the view
        /// collapses the third slider's row entirely when this is false.</summary>
        public bool HasSlider3 { get; }

        public string Slider3Label { get; }

        public double Slider3Min { get; }

        public double Slider3Max { get; }

        [ObservableProperty]
        private double slider3Value;

        public SimpleAdjustmentViewModel(SimpleAdjustmentKind kind)
        {
            Kind = kind;

            switch (kind)
            {
                case SimpleAdjustmentKind.BrightnessContrast:
                    Title = "Brightness / Contrast";
                    Slider1Label = "Brightness"; Slider1Min = -100; Slider1Max = 100;
                    Slider2Label = "Contrast"; Slider2Min = -100; Slider2Max = 100;
                    HasSlider3 = false;
                    Slider3Label = string.Empty;
                    break;

                case SimpleAdjustmentKind.HueSaturation:
                    Title = "Hue / Saturation / Lightness";
                    Slider1Label = "Hue"; Slider1Min = -180; Slider1Max = 180;
                    Slider2Label = "Saturation"; Slider2Min = -100; Slider2Max = 100;
                    Slider3Label = "Lightness"; Slider3Min = -100; Slider3Max = 100;
                    HasSlider3 = true;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public SKBitmap Apply(SKBitmap source) => Kind switch
        {
            SimpleAdjustmentKind.BrightnessContrast => BrightnessContrastAdjustment.BuildLut(Slider1Value, Slider2Value).Apply(source),
            SimpleAdjustmentKind.HueSaturation => HueSaturationAdjustment.Apply(source, Slider1Value, Slider2Value, Slider3Value),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}
