using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Engine.Text
{
    /// <summary>
    /// Text to 3D: glyph outlines from an installed font (WPF's FormattedText), flattened
    /// to polygons and extruded with <see cref="ShapeExtruder"/>. The mesh is centred on
    /// the origin, Y up, letters face +Z; <paramref name="height"/> is the font's em size
    /// in world units.
    /// </summary>
    public static class TextOutline
    {
        public static IReadOnlyList<IReadOnlyList<Vector2>> Contours(string text, string fontFamily, float height, bool bold = false, bool italic = false)
        {
            const double em = 100.0;
            var typeface = new Typeface(new FontFamily(fontFamily), italic ? FontStyles.Italic : FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var formatted = new FormattedText(text ?? string.Empty, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, em, Brushes.Black, 1.0);
            var geometry = formatted.BuildGeometry(new Point(0, 0));
            var flat = geometry.GetFlattenedPathGeometry(0.15, ToleranceType.Absolute);

            var scale = height / (float)em;
            var contours = new List<IReadOnlyList<Vector2>>();
            foreach (var figure in flat.Figures)
            {
                var points = new List<Vector2> { Convert(figure.StartPoint) };
                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case LineSegment line: points.Add(Convert(line.Point)); break;
                        case PolyLineSegment poly: points.AddRange(poly.Points.Select(Convert)); break;
                    }
                }
                if (points.Count >= 3) contours.Add(points);
            }
            if (contours.Count == 0) return contours;

            // Centre on the origin.
            var all = contours.SelectMany(c => c).ToList();
            var center = (all.Aggregate(Vector2.Min) + all.Aggregate(Vector2.Max)) * 0.5f;
            return contours.Select(c => (IReadOnlyList<Vector2>)c.Select(p => p - center).ToList()).ToList();

            // WPF is Y-down; flip to Y-up.
            Vector2 Convert(Point p) => new((float)p.X * scale, -(float)p.Y * scale);
        }

        public static Mesh BuildMesh(string text, string fontFamily, float height, float depth, bool bold = false, bool italic = false)
        {
            var mesh = ShapeExtruder.Extrude(Contours(text, fontFamily, height, bold, italic), depth, string.IsNullOrWhiteSpace(text) ? "Text" : text.Trim());
            mesh.Material = new Core.Materials.Material("Text");
            return mesh;
        }
    }
}
