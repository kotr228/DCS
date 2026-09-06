using System;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Adjustments;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Backs <see cref="Views.LevelsDialog"/>: input/output black/white points, gamma,
    /// and a live luminance histogram computed once from the layer's pixels at the
    /// moment the dialog opens (the histogram shows the *original*, pre-adjustment
    /// distribution throughout - exactly what every real Levels dialog does, so the
    /// shape you're dragging sliders against never moves out from under you).
    /// </summary>
    public partial class LevelsViewModel : ObservableObject
    {
        [ObservableProperty]
        private int inputBlack;

        [ObservableProperty]
        private int inputWhite = 255;

        [ObservableProperty]
        private double gamma = 1.0;

        [ObservableProperty]
        private int outputBlack;

        [ObservableProperty]
        private int outputWhite = 255;

        /// <summary>The histogram's outline as a WPF <c>Polygon.Points</c>-format
        /// string in a fixed 0-255 (x) by 0-100 (y, inverted so a taller bar is a
        /// smaller y) coordinate space - the view stretches this to fill whatever
        /// size it actually renders the graph at, so no pixel-size math needs to
        /// happen here.</summary>
        public string HistogramPoints { get; }

        public LevelsViewModel(SKBitmap source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var luminance = Histogram.Compute(source).Luminance;
            var max = Math.Max(1, luminance.Max());

            var sb = new StringBuilder();
            sb.Append("0,100 ");
            for (var i = 0; i < 256; i++)
            {
                var normalized = luminance[i] / (double)max;
                var y = 100.0 - normalized * 100.0;
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(y.ToString(CultureInfo.InvariantCulture));
                sb.Append(' ');
            }
            sb.Append("255,100");
            HistogramPoints = sb.ToString();
        }

        public SKBitmap Apply(SKBitmap source) =>
            LevelsAdjustment.BuildLut(InputBlack, InputWhite, Gamma, OutputBlack, OutputWhite).Apply(source);
    }
}
