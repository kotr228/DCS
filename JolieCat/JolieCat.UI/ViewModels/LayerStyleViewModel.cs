using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Effects;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// The Layer Style dialog: a checkbox and settings per effect. Every change raises
    /// PropertyChanged, which MainViewModel turns into a live canvas preview;
    /// <see cref="ToStyle"/> gives the resulting <see cref="LayerStyle"/>.
    /// Colors are edited as #RRGGBB text; the "FG" buttons copy the foreground color.
    /// </summary>
    public partial class LayerStyleViewModel : ObservableObject
    {
        private readonly SKColor _foreground;

        public LayerStyleViewModel(LayerStyle? style, SKColor foreground)
        {
            _foreground = foreground;
            var s = style ?? new LayerStyle();

            dropShadow = s.DropShadow;
            shadowColor = Hex(s.ShadowColor);
            shadowOpacity = s.ShadowOpacity;
            shadowAngle = s.ShadowAngle;
            shadowDistance = s.ShadowDistance;
            shadowSize = s.ShadowSize;

            outerGlow = s.OuterGlow;
            glowColor = Hex(s.GlowColor);
            glowOpacity = s.GlowOpacity;
            glowSize = s.GlowSize;

            stroke = s.Stroke;
            strokeColor = Hex(s.StrokeColor);
            strokeSize = s.StrokeSize;
            strokeOpacity = s.StrokeOpacity;

            colorOverlay = s.ColorOverlay;
            overlayColor = Hex(s.OverlayColor);
            overlayOpacity = s.OverlayOpacity;

            innerShadow = s.InnerShadow;
            innerShadowColor = Hex(s.InnerShadowColor);
            innerShadowOpacity = s.InnerShadowOpacity;
            innerShadowAngle = s.InnerShadowAngle;
            innerShadowDistance = s.InnerShadowDistance;
            innerShadowSize = s.InnerShadowSize;

            innerGlow = s.InnerGlow;
            innerGlowColor = Hex(s.InnerGlowColor);
            innerGlowOpacity = s.InnerGlowOpacity;
            innerGlowSize = s.InnerGlowSize;

            gradientOverlay = s.GradientOverlay;
            gradientStart = Hex(s.GradientStart);
            gradientEnd = Hex(s.GradientEnd);
            gradientOpacity = s.GradientOpacity;
            gradientAngle = s.GradientAngle;

            patternOverlay = s.PatternOverlay;
            patternName = s.PatternName;
            patternColor = Hex(s.PatternColor);
            patternScale = s.PatternScale;
            patternOpacity = s.PatternOpacity;

            bevel = s.Bevel;
            bevelSize = s.BevelSize;
            bevelDepth = s.BevelDepth;
            bevelAngle = s.BevelAngle;
            bevelHighlight = Hex(s.BevelHighlight);
            bevelHighlightOpacity = s.BevelHighlightOpacity;
            bevelShadow = Hex(s.BevelShadow);
            bevelShadowOpacity = s.BevelShadowOpacity;
        }

        [ObservableProperty] private bool dropShadow;
        [ObservableProperty] private string shadowColor;
        [ObservableProperty] private double shadowOpacity;
        [ObservableProperty] private double shadowAngle;
        [ObservableProperty] private double shadowDistance;
        [ObservableProperty] private double shadowSize;

        [ObservableProperty] private bool outerGlow;
        [ObservableProperty] private string glowColor;
        [ObservableProperty] private double glowOpacity;
        [ObservableProperty] private double glowSize;

        [ObservableProperty] private bool stroke;
        [ObservableProperty] private string strokeColor;
        [ObservableProperty] private double strokeSize;
        [ObservableProperty] private double strokeOpacity;

        [ObservableProperty] private bool colorOverlay;
        [ObservableProperty] private string overlayColor;
        [ObservableProperty] private double overlayOpacity;

        [ObservableProperty] private bool innerShadow;
        [ObservableProperty] private string innerShadowColor;
        [ObservableProperty] private double innerShadowOpacity;
        [ObservableProperty] private double innerShadowAngle;
        [ObservableProperty] private double innerShadowDistance;
        [ObservableProperty] private double innerShadowSize;

        [ObservableProperty] private bool innerGlow;
        [ObservableProperty] private string innerGlowColor;
        [ObservableProperty] private double innerGlowOpacity;
        [ObservableProperty] private double innerGlowSize;

        [ObservableProperty] private bool gradientOverlay;
        [ObservableProperty] private string gradientStart;
        [ObservableProperty] private string gradientEnd;
        [ObservableProperty] private double gradientOpacity;
        [ObservableProperty] private double gradientAngle;

        [ObservableProperty] private bool patternOverlay;
        [ObservableProperty] private string patternName;
        [ObservableProperty] private string patternColor;
        [ObservableProperty] private double patternScale;
        [ObservableProperty] private double patternOpacity;

        public System.Collections.Generic.IReadOnlyList<string> PatternNames
        {
            get
            {
                Media.PatternLibrary.EnsureLoaded();
                return PatternTiles.Names;
            }
        }

        [ObservableProperty] private bool bevel;
        [ObservableProperty] private double bevelSize;
        [ObservableProperty] private double bevelDepth;
        [ObservableProperty] private double bevelAngle;
        [ObservableProperty] private string bevelHighlight;
        [ObservableProperty] private double bevelHighlightOpacity;
        [ObservableProperty] private string bevelShadow;
        [ObservableProperty] private double bevelShadowOpacity;

        // Editing a setting turns its effect on - like clicking into a Photoshop effect.
        partial void OnShadowColorChanged(string value) => DropShadow = true;
        partial void OnShadowOpacityChanged(double value) => DropShadow = true;
        partial void OnShadowAngleChanged(double value) => DropShadow = true;
        partial void OnShadowDistanceChanged(double value) => DropShadow = true;
        partial void OnShadowSizeChanged(double value) => DropShadow = true;
        partial void OnGlowColorChanged(string value) => OuterGlow = true;
        partial void OnGlowOpacityChanged(double value) => OuterGlow = true;
        partial void OnGlowSizeChanged(double value) => OuterGlow = true;
        partial void OnStrokeColorChanged(string value) => Stroke = true;
        partial void OnStrokeSizeChanged(double value) => Stroke = true;
        partial void OnStrokeOpacityChanged(double value) => Stroke = true;
        partial void OnOverlayColorChanged(string value) => ColorOverlay = true;
        partial void OnOverlayOpacityChanged(double value) => ColorOverlay = true;
        partial void OnInnerShadowColorChanged(string value) => InnerShadow = true;
        partial void OnInnerShadowOpacityChanged(double value) => InnerShadow = true;
        partial void OnInnerShadowAngleChanged(double value) => InnerShadow = true;
        partial void OnInnerShadowDistanceChanged(double value) => InnerShadow = true;
        partial void OnInnerShadowSizeChanged(double value) => InnerShadow = true;
        partial void OnInnerGlowColorChanged(string value) => InnerGlow = true;
        partial void OnInnerGlowOpacityChanged(double value) => InnerGlow = true;
        partial void OnInnerGlowSizeChanged(double value) => InnerGlow = true;
        partial void OnGradientStartChanged(string value) => GradientOverlay = true;
        partial void OnGradientEndChanged(string value) => GradientOverlay = true;
        partial void OnGradientOpacityChanged(double value) => GradientOverlay = true;
        partial void OnGradientAngleChanged(double value) => GradientOverlay = true;
        partial void OnPatternNameChanged(string value) => PatternOverlay = true;
        partial void OnPatternColorChanged(string value) => PatternOverlay = true;
        partial void OnPatternScaleChanged(double value) => PatternOverlay = true;
        partial void OnPatternOpacityChanged(double value) => PatternOverlay = true;
        partial void OnBevelSizeChanged(double value) => Bevel = true;
        partial void OnBevelDepthChanged(double value) => Bevel = true;
        partial void OnBevelAngleChanged(double value) => Bevel = true;
        partial void OnBevelHighlightChanged(string value) => Bevel = true;
        partial void OnBevelHighlightOpacityChanged(double value) => Bevel = true;
        partial void OnBevelShadowChanged(string value) => Bevel = true;
        partial void OnBevelShadowOpacityChanged(double value) => Bevel = true;

        [RelayCommand]
        private void UseForeground(string? effect)
        {
            var hex = Hex(_foreground);
            switch (effect)
            {
                case "Shadow": ShadowColor = hex; break;
                case "Glow": GlowColor = hex; break;
                case "Stroke": StrokeColor = hex; break;
                case "Overlay": OverlayColor = hex; break;
                case "InnerShadow": InnerShadowColor = hex; break;
                case "InnerGlow": InnerGlowColor = hex; break;
                case "GradientStart": GradientStart = hex; break;
                case "GradientEnd": GradientEnd = hex; break;
                case "BevelHighlight": BevelHighlight = hex; break;
                case "Pattern": PatternColor = hex; break;
                case "BevelShadow": BevelShadow = hex; break;
            }
        }

        [RelayCommand]
        private void ClearAll()
        {
            DropShadow = false;
            OuterGlow = false;
            Stroke = false;
            ColorOverlay = false;
            InnerShadow = false;
            InnerGlow = false;
            GradientOverlay = false;
            PatternOverlay = false;
            Bevel = false;
        }

        public LayerStyle ToStyle() => new()
        {
            DropShadow = DropShadow,
            ShadowColor = Parse(ShadowColor, SKColors.Black),
            ShadowOpacity = ShadowOpacity,
            ShadowAngle = ShadowAngle,
            ShadowDistance = ShadowDistance,
            ShadowSize = ShadowSize,
            OuterGlow = OuterGlow,
            GlowColor = Parse(GlowColor, new SKColor(255, 255, 190)),
            GlowOpacity = GlowOpacity,
            GlowSize = GlowSize,
            Stroke = Stroke,
            StrokeColor = Parse(StrokeColor, SKColors.Black),
            StrokeSize = StrokeSize,
            StrokeOpacity = StrokeOpacity,
            ColorOverlay = ColorOverlay,
            OverlayColor = Parse(OverlayColor, new SKColor(255, 0, 0)),
            OverlayOpacity = OverlayOpacity,
            InnerShadow = InnerShadow,
            InnerShadowColor = Parse(InnerShadowColor, SKColors.Black),
            InnerShadowOpacity = InnerShadowOpacity,
            InnerShadowAngle = InnerShadowAngle,
            InnerShadowDistance = InnerShadowDistance,
            InnerShadowSize = InnerShadowSize,
            InnerGlow = InnerGlow,
            InnerGlowColor = Parse(InnerGlowColor, new SKColor(255, 255, 190)),
            InnerGlowOpacity = InnerGlowOpacity,
            InnerGlowSize = InnerGlowSize,
            GradientOverlay = GradientOverlay,
            GradientStart = Parse(GradientStart, SKColors.Black),
            GradientEnd = Parse(GradientEnd, SKColors.White),
            GradientOpacity = GradientOpacity,
            GradientAngle = GradientAngle,
            PatternOverlay = PatternOverlay,
            PatternName = PatternName,
            PatternColor = Parse(PatternColor, SKColors.Black),
            PatternScale = PatternScale,
            PatternOpacity = PatternOpacity,
            Bevel = Bevel,
            BevelSize = BevelSize,
            BevelDepth = BevelDepth,
            BevelAngle = BevelAngle,
            BevelHighlight = Parse(BevelHighlight, SKColors.White),
            BevelHighlightOpacity = BevelHighlightOpacity,
            BevelShadow = Parse(BevelShadow, SKColors.Black),
            BevelShadowOpacity = BevelShadowOpacity,
        };

        internal static string Hex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

        /// <summary>Accepts "#RRGGBB", "RRGGBB" or "#RGB"; anything else keeps the fallback
        /// so a half-typed value doesn't break the preview.</summary>
        internal static SKColor Parse(string? text, SKColor fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            var t = text.Trim().TrimStart('#');
            if (t.Length == 3) t = string.Concat(t[0], t[0], t[1], t[1], t[2], t[2]);
            if (t.Length != 6 || !uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                return fallback;
            return new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
    }
}
