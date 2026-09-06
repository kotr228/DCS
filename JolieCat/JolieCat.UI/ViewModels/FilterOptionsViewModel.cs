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
    }

    /// <summary>
    /// Backs <see cref="Views.FilterDialog"/> - the shared shell for every filter in
    /// this round (Gaussian Blur, Box Blur, Sharpen, Noise), each just a single
    /// "amount" slider whose label/range/effect depend on <see cref="Kind"/>. One
    /// dialog rather than four near-identical ones.
    /// </summary>
    public partial class FilterOptionsViewModel : ObservableObject
    {
        public FilterKind Kind { get; }

        public string Title { get; }

        public string AmountLabel { get; }

        public double AmountMin { get; }

        public double AmountMax { get; }

        [ObservableProperty]
        private double amount;

        public FilterOptionsViewModel(FilterKind kind)
        {
            Kind = kind;

            switch (kind)
            {
                case FilterKind.GaussianBlur:
                    Title = "Gaussian Blur";
                    AmountLabel = "Radius";
                    AmountMin = 0;
                    AmountMax = 50;
                    amount = 5;
                    break;

                case FilterKind.BoxBlur:
                    Title = "Box Blur";
                    AmountLabel = "Radius";
                    AmountMin = 0;
                    AmountMax = 25;
                    amount = 3;
                    break;

                case FilterKind.Sharpen:
                    Title = "Sharpen";
                    AmountLabel = "Amount";
                    AmountMin = 0;
                    AmountMax = 3;
                    amount = 1;
                    break;

                case FilterKind.Noise:
                    Title = "Noise";
                    AmountLabel = "Intensity";
                    AmountMin = 0;
                    AmountMax = 100;
                    amount = 20;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>Applies this filter at its current <see cref="Amount"/> to
        /// <paramref name="source"/>, returning a new bitmap - called fresh against
        /// the untouched original snapshot on every preview update and again once,
        /// identically, at commit (see <c>CanvasViewModel.BeginAdjustment</c>'s own
        /// remarks for why that's always safe to do this way).</summary>
        public SKBitmap Apply(SKBitmap source) => Kind switch
        {
            FilterKind.GaussianBlur => BlurFilter.Apply(source, BlurType.Gaussian, (float)Amount),
            FilterKind.BoxBlur => BlurFilter.Apply(source, BlurType.Box, (float)Amount),
            FilterKind.Sharpen => SharpenFilter.Apply(source, (float)Amount),
            // A fixed seed - not a fresh random pattern every preview frame - so the
            // live preview holds still while only the intensity slider is what's
            // actually changing, rather than the whole grain pattern also "swimming"
            // with every repaint at the same intensity.
            FilterKind.Noise => NoiseFilter.Apply(source, (float)Amount, seed: 12345),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}
