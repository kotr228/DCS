using System;
using System.Collections.Generic;
using System.Globalization;
using SkiaSharp;

namespace JolieCat.Core.Effects
{
    /// <summary>
    /// A layer's live effects (Photoshop's "Layer Style"): Drop Shadow, Outer Glow,
    /// Stroke and Color Overlay. They are drawn by the compositor from the layer's
    /// pixels every time, so painting on the layer updates them automatically.
    /// Immutable - editing swaps in a new instance, which keeps undo simple.
    /// </summary>
    public sealed record LayerStyle
    {
        // ----- Drop Shadow -----
        public bool DropShadow { get; init; }
        public SKColor ShadowColor { get; init; } = SKColors.Black;
        /// <summary>0..100 %</summary>
        public double ShadowOpacity { get; init; } = 75;
        /// <summary>Direction the light comes from, degrees (Photoshop: 120 = from top-left).</summary>
        public double ShadowAngle { get; init; } = 120;
        public double ShadowDistance { get; init; } = 8;
        public double ShadowSize { get; init; } = 8;

        // ----- Outer Glow -----
        public bool OuterGlow { get; init; }
        public SKColor GlowColor { get; init; } = new(255, 255, 190);
        public double GlowOpacity { get; init; } = 75;
        public double GlowSize { get; init; } = 10;

        // ----- Stroke (outside) -----
        public bool Stroke { get; init; }
        public SKColor StrokeColor { get; init; } = SKColors.Black;
        public double StrokeSize { get; init; } = 3;
        public double StrokeOpacity { get; init; } = 100;

        // ----- Color Overlay -----
        public bool ColorOverlay { get; init; }
        public SKColor OverlayColor { get; init; } = new(255, 0, 0);
        public double OverlayOpacity { get; init; } = 100;

        // ----- Inner Shadow -----
        public bool InnerShadow { get; init; }
        public SKColor InnerShadowColor { get; init; } = SKColors.Black;
        public double InnerShadowOpacity { get; init; } = 75;
        public double InnerShadowAngle { get; init; } = 120;
        public double InnerShadowDistance { get; init; } = 5;
        public double InnerShadowSize { get; init; } = 5;

        // ----- Inner Glow -----
        public bool InnerGlow { get; init; }
        public SKColor InnerGlowColor { get; init; } = new(255, 255, 190);
        public double InnerGlowOpacity { get; init; } = 75;
        public double InnerGlowSize { get; init; } = 10;

        // ----- Gradient Overlay -----
        public bool GradientOverlay { get; init; }
        public SKColor GradientStart { get; init; } = SKColors.Black;
        public SKColor GradientEnd { get; init; } = SKColors.White;
        public double GradientOpacity { get; init; } = 100;
        /// <summary>Direction of the gradient, degrees (90 = from the bottom up, like Photoshop).</summary>
        public double GradientAngle { get; init; } = 90;

        // ----- Pattern Overlay -----
        public bool PatternOverlay { get; init; }
        /// <summary>One of <see cref="PatternTiles.Names"/>.</summary>
        public string PatternName { get; init; } = "Checker";
        public SKColor PatternColor { get; init; } = SKColors.Black;
        /// <summary>Scale, % (100 = 16-pixel tiles).</summary>
        public double PatternScale { get; init; } = 100;
        public double PatternOpacity { get; init; } = 50;

        // ----- Bevel & Emboss (inner bevel) -----
        public bool Bevel { get; init; }
        public double BevelSize { get; init; } = 5;
        /// <summary>Depth, %: how far the lit and dark edges reach in.</summary>
        public double BevelDepth { get; init; } = 100;
        /// <summary>Direction the light comes from, degrees.</summary>
        public double BevelAngle { get; init; } = 120;
        public SKColor BevelHighlight { get; init; } = SKColors.White;
        public double BevelHighlightOpacity { get; init; } = 75;
        public SKColor BevelShadow { get; init; } = SKColors.Black;
        public double BevelShadowOpacity { get; init; } = 75;

        public bool HasAnyEffect => DropShadow || OuterGlow || Stroke || ColorOverlay || InnerShadow || InnerGlow || GradientOverlay || PatternOverlay || Bevel;

        /// <summary>True when some effect is painted inside the layer's own pixels.</summary>
        public bool HasInnerEffect => ColorOverlay || InnerShadow || InnerGlow || GradientOverlay || PatternOverlay || Bevel;

        /// <summary>Inner shadow offset: it falls away from the light, like the drop shadow.</summary>
        public SKPoint InnerShadowOffset => Offset(InnerShadowAngle, InnerShadowDistance);

        /// <summary>How far the bevel's lit and dark edges are shifted.</summary>
        public SKPoint BevelOffset => Offset(BevelAngle, Math.Max(1, BevelSize * BevelDepth / 100.0));

        internal static SKPoint Offset(double angle, double distance)
        {
            var radians = angle * Math.PI / 180.0;
            return new SKPoint((float)(-Math.Cos(radians) * distance), (float)(Math.Sin(radians) * distance));
        }

        /// <summary>Shadow offset in document pixels. The shadow falls away from the light.</summary>
        public SKPoint ShadowOffset
        {
            get
            {
                var radians = ShadowAngle * Math.PI / 180.0;
                return new SKPoint((float)(-Math.Cos(radians) * ShadowDistance), (float)(Math.Sin(radians) * ShadowDistance));
            }
        }

        /// <summary>How far outside the layer's pixels the effects can reach.</summary>
        public float Reach
        {
            get
            {
                var reach = 0.0;
                if (DropShadow) reach = Math.Max(reach, ShadowDistance + ShadowSize * 1.5);
                if (OuterGlow) reach = Math.Max(reach, GlowSize * 1.5);
                if (Stroke) reach = Math.Max(reach, StrokeSize);
                return (float)Math.Ceiling(reach);
            }
        }

        internal static byte Alpha(double percent) => (byte)Math.Clamp(Math.Round(percent * 2.55), 0, 255);

        // Photoshop's "Size" is roughly the blur's visible radius; Skia wants a sigma.
        internal static float Sigma(double size) => (float)Math.Max(0.01, size / 2.5);

        // ----- Serialization: "key=value;key=value" -----

        public string Serialize()
        {
            var c = CultureInfo.InvariantCulture;
            var parts = new List<string>();
            if (DropShadow) parts.Add(string.Create(c, $"shadow={Hex(ShadowColor)},{ShadowOpacity},{ShadowAngle},{ShadowDistance},{ShadowSize}"));
            if (OuterGlow) parts.Add(string.Create(c, $"glow={Hex(GlowColor)},{GlowOpacity},{GlowSize}"));
            if (Stroke) parts.Add(string.Create(c, $"stroke={Hex(StrokeColor)},{StrokeSize},{StrokeOpacity}"));
            if (ColorOverlay) parts.Add(string.Create(c, $"overlay={Hex(OverlayColor)},{OverlayOpacity}"));
            if (InnerShadow) parts.Add(string.Create(c, $"ishadow={Hex(InnerShadowColor)},{InnerShadowOpacity},{InnerShadowAngle},{InnerShadowDistance},{InnerShadowSize}"));
            if (InnerGlow) parts.Add(string.Create(c, $"iglow={Hex(InnerGlowColor)},{InnerGlowOpacity},{InnerGlowSize}"));
            if (GradientOverlay) parts.Add(string.Create(c, $"gradient={Hex(GradientStart)},{Hex(GradientEnd)},{GradientOpacity},{GradientAngle}"));
            if (PatternOverlay) parts.Add(string.Create(c, $"pattern={PatternName},{Hex(PatternColor)},{PatternScale},{PatternOpacity}"));
            if (Bevel) parts.Add(string.Create(c, $"bevel={BevelSize},{BevelDepth},{BevelAngle},{Hex(BevelHighlight)},{BevelHighlightOpacity},{Hex(BevelShadow)},{BevelShadowOpacity}"));
            return string.Join(";", parts);
        }

        public static LayerStyle? Deserialize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var style = new LayerStyle();
            foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq < 0) continue;
                var key = part[..eq];
                var v = part[(eq + 1)..].Split(',');
                try
                {
                    style = key switch
                    {
                        "shadow" when v.Length >= 5 => style with
                        {
                            DropShadow = true, ShadowColor = SKColor.Parse(v[0]), ShadowOpacity = D(v[1]),
                            ShadowAngle = D(v[2]), ShadowDistance = D(v[3]), ShadowSize = D(v[4]),
                        },
                        "glow" when v.Length >= 3 => style with { OuterGlow = true, GlowColor = SKColor.Parse(v[0]), GlowOpacity = D(v[1]), GlowSize = D(v[2]) },
                        "stroke" when v.Length >= 3 => style with { Stroke = true, StrokeColor = SKColor.Parse(v[0]), StrokeSize = D(v[1]), StrokeOpacity = D(v[2]) },
                        "overlay" when v.Length >= 2 => style with { ColorOverlay = true, OverlayColor = SKColor.Parse(v[0]), OverlayOpacity = D(v[1]) },
                        "ishadow" when v.Length >= 5 => style with
                        {
                            InnerShadow = true, InnerShadowColor = SKColor.Parse(v[0]), InnerShadowOpacity = D(v[1]),
                            InnerShadowAngle = D(v[2]), InnerShadowDistance = D(v[3]), InnerShadowSize = D(v[4]),
                        },
                        "iglow" when v.Length >= 3 => style with { InnerGlow = true, InnerGlowColor = SKColor.Parse(v[0]), InnerGlowOpacity = D(v[1]), InnerGlowSize = D(v[2]) },
                        "gradient" when v.Length >= 4 => style with
                        {
                            GradientOverlay = true, GradientStart = SKColor.Parse(v[0]), GradientEnd = SKColor.Parse(v[1]),
                            GradientOpacity = D(v[2]), GradientAngle = D(v[3]),
                        },
                        "pattern" when v.Length >= 4 => style with
                        {
                            PatternOverlay = true, PatternName = v[0], PatternColor = SKColor.Parse(v[1]),
                            PatternScale = D(v[2]), PatternOpacity = D(v[3]),
                        },
                        "bevel" when v.Length >= 7 => style with
                        {
                            Bevel = true, BevelSize = D(v[0]), BevelDepth = D(v[1]), BevelAngle = D(v[2]),
                            BevelHighlight = SKColor.Parse(v[3]), BevelHighlightOpacity = D(v[4]),
                            BevelShadow = SKColor.Parse(v[5]), BevelShadowOpacity = D(v[6]),
                        },
                        _ => style,
                    };
                }
                catch (FormatException)
                {
                    // An unreadable effect is skipped rather than failing the whole file.
                }
            }

            return style.HasAnyEffect ? style : null;
        }

        private static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static string Hex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
    }
}
