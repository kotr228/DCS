using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Adjustments;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Backs <see cref="Views.CurvesDialog"/>'s interactive spline graph: an ordered
    /// set of control points (always starting as the flat (0,0)-(255,255) identity
    /// diagonal - a straight line, not a bend, since <see cref="CurvesAdjustment"/>'s
    /// own boundary handling was specifically fixed this round to make that case a
    /// true no-op), the smooth curve line through them, and applying that curve to a
    /// bitmap via the same <see cref="CurvesAdjustment"/> Core logic
    /// <see cref="ViewModels.CurvePointViewModel"/>'s own X/Y feed.
    /// </summary>
    public partial class CurvesViewModel : ObservableObject
    {
        /// <summary>At least this many points always remain - removing down to just
        /// two (the identity diagonal's own endpoints) still describes a well-defined
        /// curve; fewer would not.</summary>
        private const int MinimumPoints = 2;

        public ObservableCollection<CurvePointViewModel> Points { get; } = new();

        /// <summary>The curve's own line, sampled at every input value (0-255) and
        /// formatted the same way <see cref="LevelsViewModel.HistogramPoints"/> is -
        /// a WPF <c>Polyline.Points</c>-format string, recomputed whenever a point
        /// moves, is added, or is removed.</summary>
        [ObservableProperty]
        private string curveLinePoints = string.Empty;

        public CurvesViewModel()
        {
            var black = new CurvePointViewModel(0, 0);
            var white = new CurvePointViewModel(255, 255);
            black.PropertyChanged += OnPointPropertyChanged;
            white.PropertyChanged += OnPointPropertyChanged;
            Points.Add(black);
            Points.Add(white);

            RebuildCurveLine();
        }

        /// <summary>Adds a new control point at (<paramref name="x"/>, <paramref name="y"/>) -
        /// the graph background's own click handler in the view's code-behind.</summary>
        public void AddPoint(double x, double y)
        {
            x = Math.Clamp(x, 0, 255);
            y = Math.Clamp(y, 0, 255);

            var point = new CurvePointViewModel(x, y);
            point.PropertyChanged += OnPointPropertyChanged;

            var insertAt = Points.Count;
            for (var i = 0; i < Points.Count; i++)
            {
                if (Points[i].X > x) { insertAt = i; break; }
            }
            Points.Insert(insertAt, point);

            RebuildCurveLine();
        }

        /// <summary>Removes <paramref name="point"/> - a no-op if only
        /// <see cref="MinimumPoints"/> remain, so the curve can never collapse to
        /// zero or one point (which <see cref="CurvesAdjustment"/> can't build a
        /// meaningful LUT from).</summary>
        public void RemovePoint(CurvePointViewModel point)
        {
            if (Points.Count <= MinimumPoints) return;

            point.PropertyChanged -= OnPointPropertyChanged;
            Points.Remove(point);
            RebuildCurveLine();
        }

        private void OnPointPropertyChanged(object? sender, PropertyChangedEventArgs e) => RebuildCurveLine();

        private void RebuildCurveLine()
        {
            var controlPoints = Points.Select(p => (p.X, p.Y)).ToList();
            var lut = CurvesAdjustment.BuildLut(controlPoints);

            var sb = new StringBuilder();
            for (var i = 0; i < 256; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((255 - lut.Red[i]).ToString(CultureInfo.InvariantCulture));
            }
            CurveLinePoints = sb.ToString();
        }

        public SKBitmap Apply(SKBitmap source) =>
            CurvesAdjustment.BuildLut(Points.Select(p => (p.X, p.Y)).ToList()).Apply(source);
    }
}
