using System;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public enum FilterKind
    {
        GaussianBlur,
        BoxBlur,
        Sharpen,
        Noise,
        Posterize,
        Threshold,
        Pixelate,
        Emboss,
        Vignette,
        MotionBlur,
        UnsharpMask,
        Median,
        Vibrance,
        Twirl,
        Pinch,
        Wave,
        RadialBlur,
        SurfaceBlur,
        HighPass,
        Spherize,
        Ripple,
        OilPaint,
        SmartSharpen,

        // No settings: applied immediately, without a dialog.
        Invert,
        Desaturate,
        FindEdges,
        AutoContrast,
        AutoColor,
        Equalize,

        /// <summary>Uses the foreground/background colors (packed into Amount/SecondAmount).</summary>
        Clouds,
    }

    /// <summary>
    /// Backs <see cref="Views.FilterDialog"/> - the shared shell for every filter with a
    /// single "amount" slider whose label/range/effect depend on <see cref="Kind"/>.
    /// Filters without settings (<see cref="HasSettings"/> false) are applied directly
    /// by <c>MainViewModel.OpenFilterDialog</c> without showing the dialog.
    /// </summary>
    public partial class FilterOptionsViewModel : ObservableObject
    {
        public FilterKind Kind { get; }

        public string Title { get; }

        public string AmountLabel { get; } = string.Empty;

        public double AmountMin { get; }

        public double AmountMax { get; }

        public string Suffix { get; } = string.Empty;

        public bool HasSettings { get; } = true;

        [ObservableProperty]
        private double amount;

        /// <summary>Optional second setting (Motion Blur's angle, Unsharp Mask's amount).</summary>
        public bool HasSecondAmount { get; }

        public string SecondAmountLabel { get; } = string.Empty;

        public double SecondAmountMin { get; }

        public double SecondAmountMax { get; }

        public string SecondSuffix { get; } = string.Empty;

        [ObservableProperty]
        private double secondAmount;

        /// <summary>The filter's display name (for the History panel).</summary>
        public static string TitleOf(FilterKind kind)
        {
            var title = new FilterOptionsViewModel(kind).Title;
            return string.IsNullOrEmpty(title)
                ? System.Text.RegularExpressions.Regex.Replace(kind.ToString(), "(?<=[a-z])(?=[A-Z])", " ")
                : title;
        }

        public FilterOptionsViewModel(FilterKind kind)
        {
            Kind = kind;

            switch (kind)
            {
                case FilterKind.GaussianBlur:
                    Title = "Gaussian Blur";
                    AmountLabel = "Radius";
                    AmountMax = 100;
                    Suffix = "px";
                    amount = 5;
                    break;

                case FilterKind.BoxBlur:
                    Title = "Box Blur";
                    AmountLabel = "Radius";
                    AmountMax = 100;
                    Suffix = "px";
                    amount = 3;
                    break;

                case FilterKind.Sharpen:
                    Title = "Sharpen";
                    AmountLabel = "Amount";
                    AmountMax = 3;
                    amount = 1;
                    break;

                case FilterKind.Noise:
                    Title = "Noise";
                    AmountLabel = "Intensity";
                    AmountMax = 100;
                    Suffix = "%";
                    amount = 20;
                    break;

                case FilterKind.Posterize:
                    Title = "Posterize";
                    AmountLabel = "Levels";
                    AmountMin = 2;
                    AmountMax = 32;
                    amount = 6;
                    break;

                case FilterKind.Threshold:
                    Title = "Threshold";
                    AmountLabel = "Level";
                    AmountMax = 255;
                    amount = 128;
                    break;

                case FilterKind.Pixelate:
                    Title = "Pixelate";
                    AmountLabel = "Cell size";
                    AmountMin = 2;
                    AmountMax = 128;
                    Suffix = "px";
                    amount = 8;
                    break;

                case FilterKind.Emboss:
                    Title = "Emboss";
                    AmountLabel = "Strength";
                    AmountMin = 0.1;
                    AmountMax = 5;
                    amount = 1;
                    break;

                case FilterKind.Vignette:
                    Title = "Vignette";
                    AmountLabel = "Amount";
                    AmountMax = 100;
                    Suffix = "%";
                    amount = 50;
                    break;

                case FilterKind.Twirl:
                    Title = "Twirl";
                    AmountLabel = "Angle";
                    AmountMin = -999;
                    AmountMax = 999;
                    Suffix = "°";
                    amount = 180;
                    break;

                case FilterKind.Pinch:
                    Title = "Pinch";
                    AmountLabel = "Amount (negative bulges)";
                    AmountMin = -100;
                    AmountMax = 100;
                    Suffix = "%";
                    amount = 50;
                    break;

                case FilterKind.Wave:
                    Title = "Wave";
                    AmountLabel = "Amplitude";
                    AmountMin = 0;
                    AmountMax = 100;
                    Suffix = "px";
                    amount = 10;
                    HasSecondAmount = true;
                    SecondAmountLabel = "Wavelength";
                    SecondAmountMin = 4;
                    SecondAmountMax = 500;
                    SecondSuffix = "px";
                    secondAmount = 80;
                    break;

                case FilterKind.RadialBlur:
                    Title = "Radial Blur (Zoom)";
                    AmountLabel = "Amount";
                    AmountMin = 1;
                    AmountMax = 100;
                    amount = 20;
                    break;

                case FilterKind.SurfaceBlur:
                    Title = "Surface Blur";
                    AmountLabel = "Radius";
                    AmountMin = 1;
                    AmountMax = 30;
                    Suffix = "px";
                    amount = 5;
                    HasSecondAmount = true;
                    SecondAmountLabel = "Threshold";
                    SecondAmountMin = 1;
                    SecondAmountMax = 255;
                    SecondSuffix = "levels";
                    secondAmount = 15;
                    break;

                case FilterKind.HighPass:
                    Title = "High Pass";
                    AmountLabel = "Radius";
                    AmountMin = 0.5;
                    AmountMax = 100;
                    Suffix = "px";
                    amount = 10;
                    break;

                case FilterKind.MotionBlur:
                    Title = "Motion Blur";
                    AmountLabel = "Distance";
                    AmountMin = 1;
                    AmountMax = 200;
                    Suffix = "px";
                    amount = 20;
                    HasSecondAmount = true;
                    SecondAmountLabel = "Angle";
                    SecondAmountMin = -90;
                    SecondAmountMax = 90;
                    SecondSuffix = "°";
                    break;

                case FilterKind.UnsharpMask:
                    Title = "Unsharp Mask";
                    AmountLabel = "Amount";
                    AmountMin = 1;
                    AmountMax = 500;
                    Suffix = "%";
                    amount = 80;
                    HasSecondAmount = true;
                    SecondAmountLabel = "Radius";
                    SecondAmountMin = 0.1;
                    SecondAmountMax = 10;
                    SecondSuffix = "px";
                    secondAmount = 1.5;
                    break;

                case FilterKind.Median:
                    Title = "Median (Dust & Scratches)";
                    AmountLabel = "Radius";
                    AmountMin = 1;
                    AmountMax = 20;
                    Suffix = "px";
                    amount = 2;
                    break;

                case FilterKind.Vibrance:
                    Title = "Vibrance";
                    AmountLabel = "Vibrance";
                    AmountMin = -100;
                    AmountMax = 100;
                    amount = 40;
                    break;

                case FilterKind.AutoContrast:
                    Title = "Auto Contrast";
                    HasSettings = false;
                    break;

                case FilterKind.AutoColor:
                    Title = "Auto Color";
                    HasSettings = false;
                    break;

                case FilterKind.Invert:
                    Title = "Invert";
                    HasSettings = false;
                    break;

                case FilterKind.Desaturate:
                    Title = "Desaturate";
                    HasSettings = false;
                    break;

                case FilterKind.FindEdges:
                    Title = "Find Edges";
                    HasSettings = false;
                    break;

                case FilterKind.Equalize:
                    Title = "Equalize";
                    HasSettings = false;
                    break;

                case FilterKind.Clouds:
                    Title = "Clouds";
                    HasSettings = false;
                    amount = 0x000000;
                    secondAmount = 0xFFFFFF;
                    break;

                case FilterKind.Spherize:
                    Title = "Spherize";
                    AmountLabel = "Amount";
                    AmountMin = -100;
                    AmountMax = 100;
                    Suffix = "%";
                    amount = 100;
                    break;

                case FilterKind.Ripple:
                    Title = "Ripple";
                    AmountLabel = "Amount";
                    AmountMin = -999;
                    AmountMax = 999;
                    Suffix = "%";
                    amount = 100;
                    HasSecondAmount = true;
                    SecondAmountLabel = "Size";
                    SecondAmountMin = 4;
                    SecondAmountMax = 200;
                    SecondSuffix = "px";
                    secondAmount = 24;
                    break;

                case FilterKind.OilPaint:
                    Title = "Oil Paint";
                    AmountLabel = "Stylization";
                    AmountMin = 1;
                    AmountMax = 12;
                    Suffix = "px";
                    amount = 4;
                    HasSecondAmount = true;
                    SecondAmountLabel = "Detail";
                    SecondAmountMin = 4;
                    SecondAmountMax = 64;
                    secondAmount = 20;
                    break;

                case FilterKind.SmartSharpen:
                    Title = "Smart Sharpen";
                    AmountLabel = "Amount";
                    AmountMin = 0;
                    AmountMax = 500;
                    Suffix = "%";
                    amount = 150;
                    HasSecondAmount = true;
                    SecondAmountLabel = "Radius";
                    SecondAmountMin = 0.1;
                    SecondAmountMax = 64;
                    SecondSuffix = "px";
                    secondAmount = 1.5;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>Applies this filter at its current <see cref="Amount"/> to
        /// <paramref name="source"/>, returning a new bitmap - always computed from the
        /// untouched original snapshot (see <c>CanvasViewModel.BeginAdjustment</c>).</summary>
        public SKBitmap Apply(SKBitmap source) => Kind switch
        {
            FilterKind.GaussianBlur => BlurFilter.Apply(source, BlurType.Gaussian, (float)Amount),
            FilterKind.BoxBlur => BlurFilter.Apply(source, BlurType.Box, (float)Amount),
            FilterKind.Sharpen => SharpenFilter.Apply(source, (float)Amount),
            // A fixed seed so the preview grain holds still while only the slider moves.
            FilterKind.Noise => NoiseFilter.Apply(source, (float)Amount, seed: 12345),
            FilterKind.Posterize => StylizeFilters.Posterize(source, (int)Math.Round(Amount)),
            FilterKind.Threshold => StylizeFilters.Threshold(source, (int)Math.Round(Amount)),
            FilterKind.Pixelate => StylizeFilters.Pixelate(source, (int)Math.Round(Amount)),
            FilterKind.Emboss => StylizeFilters.Emboss(source, (float)Amount),
            FilterKind.Vignette => StylizeFilters.Vignette(source, (float)Amount),
            FilterKind.MotionBlur => EnhanceFilters.MotionBlur(source, (float)SecondAmount, (float)Amount),
            FilterKind.UnsharpMask => EnhanceFilters.UnsharpMask(source, (float)SecondAmount, (float)Amount),
            FilterKind.Median => EnhanceFilters.Median(source, (int)Math.Round(Amount)),
            FilterKind.Vibrance => EnhanceFilters.Vibrance(source, (float)Amount),
            FilterKind.Twirl => DistortFilters.Twirl(source, (float)Amount),
            FilterKind.Pinch => DistortFilters.Pinch(source, (float)Amount),
            FilterKind.Wave => DistortFilters.Wave(source, (float)Amount, (float)SecondAmount),
            FilterKind.RadialBlur => DistortFilters.RadialBlur(source, (float)Amount),
            FilterKind.SurfaceBlur => DistortFilters.SurfaceBlur(source, (int)Math.Round(Amount), (float)SecondAmount),
            FilterKind.HighPass => DistortFilters.HighPass(source, (float)Amount),
            FilterKind.AutoContrast => EnhanceFilters.AutoContrast(source),
            FilterKind.AutoColor => EnhanceFilters.AutoColor(source),
            FilterKind.Invert => StylizeFilters.Invert(source),
            FilterKind.Desaturate => StylizeFilters.Desaturate(source),
            FilterKind.FindEdges => StylizeFilters.FindEdges(source),
            FilterKind.Equalize => ToneFilters.Equalize(source),
            FilterKind.Spherize => ArtFilters.Spherize(source, (float)Amount),
            FilterKind.Ripple => ArtFilters.Ripple(source, (float)Amount, (float)SecondAmount),
            FilterKind.OilPaint => ArtFilters.OilPaint(source, (int)Math.Round(Amount), (int)Math.Round(SecondAmount)),
            FilterKind.SmartSharpen => ArtFilters.SmartSharpen(source, (float)Amount, (float)SecondAmount),
            FilterKind.Clouds => ArtFilters.Clouds(source,
                Core.Adjustments.AdjustmentSettings.UnpackColor(Amount), Core.Adjustments.AdjustmentSettings.UnpackColor(SecondAmount),
                HashCode.Combine((int)Amount, (int)SecondAmount, source.Width, source.Height)),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}
