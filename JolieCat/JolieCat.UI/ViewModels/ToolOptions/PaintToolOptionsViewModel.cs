using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.UI.Media;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>
    /// Options shown for every brush-like tool: Brush, Pencil, Eraser, and the
    /// retouching tools (Clone Stamp, Healing Brush, Blur, Sharpen, Sponge, Dodge, Burn).
    /// </summary>
    public partial class PaintToolOptionsViewModel : ObservableObject
    {
        [ObservableProperty]
        private double size = 24;

        [ObservableProperty]
        private double hardness = 80;

        [ObservableProperty]
        private double opacity = 100;

        /// <summary>Distance between consecutive Brush/Eraser dabs, as a percentage of
        /// the brush's own diameter - low values (well under 100) read as a smooth
        /// continuous stroke; high values (well over 100) read as a dotted/dashed one.
        /// Doesn't affect Pencil, which always draws a plain continuous hard-edged line.</summary>
        [ObservableProperty]
        private double spacing = 20;

        /// <summary>Pen pressure scales the dab size (a mouse always reports full pressure).</summary>
        [ObservableProperty]
        private bool pressureAffectsSize = true;

        /// <summary>Pen pressure scales the dab opacity.</summary>
        [ObservableProperty]
        private bool pressureAffectsOpacity;

        // ----- Brush tip and dynamics (Brush and Eraser) -----

        /// <summary>How much paint each dab lays down, %. Low flow builds up gradually,
        /// like an airbrush.</summary>
        [ObservableProperty]
        private double flow = 100;

        /// <summary>The tip's angle, degrees (matters when it isn't round).</summary>
        [ObservableProperty]
        private double angle;

        /// <summary>100 = a circle; lower = a flatter ellipse (calligraphy).</summary>
        [ObservableProperty]
        private double roundness = 100;

        /// <summary>Random size variation per dab, %.</summary>
        [ObservableProperty]
        private double sizeJitter;

        /// <summary>Random sideways spread of dabs, % of the brush size.</summary>
        [ObservableProperty]
        private double scatter;

        [ObservableProperty]
        private bool showTipSettings;

        /// <summary>
        /// Whether this tool draws with the foreground color, so its Properties panel
        /// should show the color picker. True for Brush/Pencil; false for Eraser (which
        /// only clears pixels) and the retouching tools (which don't apply a flat color).
        /// </summary>
        public bool ShowColorPicker { get; }

        /// <summary>Background Eraser: how different a color may be from the one under
        /// the brush's center and still be erased (0..255).</summary>
        [ObservableProperty]
        private double tolerance = 50;

        public bool ShowTolerance { get; init; }

        /// <summary>Pattern Stamp: the pattern it paints (built-in ones use the
        /// foreground color).</summary>
        [ObservableProperty]
        private string patternName = "Checker";

        public bool ShowPattern { get; init; }

        public System.Collections.Generic.IReadOnlyList<string> PatternNames => Core.Effects.PatternTiles.Names;

        /// <summary>Mixer Brush: 0..100, how much of the canvas's color the brush picks
        /// up as it goes (0 = plain paint, 100 = mostly smears what's there).</summary>
        [ObservableProperty]
        private double wet = 50;

        public bool ShowWet { get; init; }

        public PaintToolOptionsViewModel(bool showColorPicker = true)
        {
            ShowColorPicker = showColorPicker;
            foreach (var preset in BrushLibrary.Shared.All) Presets.Add(preset.Name);
            BrushLibrary.Shared.PresetSaved += p => { if (!Presets.Contains(p.Name)) Presets.Add(p.Name); };
        }

        /// <summary>A picture tip (alpha only) used instead of the round one, or null.</summary>
        public SkiaSharp.SKBitmap? Tip { get; private set; }

        /// <summary>The picture tip's preset name, or null for the round tip.</summary>
        [ObservableProperty]
        private string? tipName;

        /// <summary>Back to the round tip.</summary>
        [RelayCommand]
        private void UseRoundTip()
        {
            Tip?.Dispose();
            Tip = null;
            TipName = null;
        }

        /// <summary>Uses <paramref name="tip"/> (alpha only, owned here from now on) as the tip.</summary>
        public void UseTip(string name, SkiaSharp.SKBitmap tip)
        {
            Tip?.Dispose();
            Tip = tip;
            TipName = name;
        }

        public ObservableCollection<string> Presets { get; } = new();

        [RelayCommand]
        private void LoadPreset(string? name)
        {
            if (name is null || BrushLibrary.Shared.Find(name) is not { } p) return;
            Size = p.Size;
            Hardness = p.Hardness;
            Spacing = p.Spacing;
            Flow = p.Flow;
            Angle = p.Angle;
            Roundness = p.Roundness;
            SizeJitter = p.SizeJitter;
            Scatter = p.Scatter;
            if (BrushLibrary.LoadTip(p.TipFile) is { } tip) UseTip(p.Name, tip);
            else UseRoundTip();
        }

        /// <summary>Keeps the current tip settings as "My brush N".</summary>
        [RelayCommand]
        private void SavePreset()
        {
            var n = 1;
            while (Presets.Contains($"My brush {n}")) n++;
            var name = $"My brush {n}";
            var tipFile = TipName is { } tipName && BrushLibrary.Shared.Find(tipName) is { TipFile: { } file } ? file : null;
            BrushLibrary.Shared.Save(new BrushPreset(name, Size, Hardness, Spacing, Flow, Angle, Roundness, SizeJitter, Scatter, tipFile));
            if (!Presets.Contains(name)) Presets.Add(name);
        }

        [RelayCommand]
        private void DeletePreset(string? name)
        {
            if (name is null || BrushLibrary.BuiltIn.Any(p => p.Name == name)) return;
            if (BrushLibrary.Shared.Delete(name)) Presets.Remove(name);
        }
    }
}
