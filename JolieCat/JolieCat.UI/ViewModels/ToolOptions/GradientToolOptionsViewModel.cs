using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    public enum GradientKind
    {
        Linear,
        Radial,
        Angle,
        Reflected,
    }

    public enum GradientColors
    {
        ForegroundToBackground,
        ForegroundToTransparent,
        Custom,
    }

    /// <summary>One color stop of a custom gradient.</summary>
    public partial class GradientStopViewModel : ObservableObject
    {
        public GradientStopViewModel(string hex, double location, double opacity = 100)
        {
            this.hex = hex;
            this.location = location;
            this.opacity = opacity;
        }

        [ObservableProperty] private string hex;

        /// <summary>0-100%, along the gradient.</summary>
        [ObservableProperty] private double location;

        /// <summary>0-100%.</summary>
        [ObservableProperty] private double opacity;

        public SKColor Color
        {
            get
            {
                var c = LayerStyleViewModel.Parse(Hex, SKColors.Black);
                return c.WithAlpha((byte)Math.Clamp(Math.Round(Opacity * 2.55), 0, 255));
            }
        }
    }

    /// <summary>Gradient tool settings. The gradient itself is defined by dragging on
    /// the canvas: the drag's start is the first color, its end the last. "Custom"
    /// uses <see cref="Stops"/> - any number of colors, each with its own opacity.</summary>
    public partial class GradientToolOptionsViewModel : ObservableObject
    {
        public static IReadOnlyList<GradientKind> AllKinds { get; } = Enum.GetValues<GradientKind>();

        public static IReadOnlyList<GradientColors> AllColorModes { get; } = Enum.GetValues<GradientColors>();

        public GradientToolOptionsViewModel()
        {
            Stops.CollectionChanged += OnStopsChanged;
            FillStops("Spectrum");
            foreach (var name in BuiltInPresets) Presets.Add(name);
            foreach (var saved in Media.GradientLibrary.Shared.Gradients) Presets.Add(saved.Name);
        }

        [ObservableProperty]
        private GradientKind kind = GradientKind.Linear;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCustom))]
        private GradientColors colors = GradientColors.ForegroundToBackground;

        /// <summary>0-100%.</summary>
        [ObservableProperty]
        private double opacity = 100;

        [ObservableProperty]
        private bool reverse;

        public bool IsCustom => Colors == GradientColors.Custom;

        public ObservableCollection<GradientStopViewModel> Stops { get; } = new();

        /// <summary>The custom gradient, for the preview bar.</summary>
        [ObservableProperty]
        private Brush previewBrush = Brushes.Transparent;

        public static IReadOnlyList<string> BuiltInPresets { get; } = new[] { "B&W", "Spectrum", "Sunset", "Chrome", "Fade" };

        /// <summary>The preset buttons: the built-in ones, then the user's saved gradients.</summary>
        public ObservableCollection<string> Presets { get; } = new();

        /// <summary>Saves the current stops as a new preset ("My gradient N").</summary>
        [RelayCommand]
        private void SavePreset()
        {
            var n = 1;
            while (Presets.Contains($"My gradient {n}")) n++;
            var name = $"My gradient {n}";
            Media.GradientLibrary.Shared.Save(new Media.SavedGradient(name,
                Stops.OrderBy(s => s.Location).Select(s => new Media.SavedStop(s.Hex, s.Location, s.Opacity)).ToList()));
            Presets.Add(name);
        }

        /// <summary>Deletes a saved preset (the built-in ones stay).</summary>
        [RelayCommand]
        private void DeletePreset(string? name)
        {
            if (name is null || BuiltInPresets.Contains(name)) return;
            if (Media.GradientLibrary.Shared.Delete(name)) Presets.Remove(name);
        }

        /// <summary>Switches to a ready-made custom gradient.</summary>
        [RelayCommand]
        private void LoadPreset(string? name)
        {
            FillStops(name);
            Colors = GradientColors.Custom;
        }

        private void FillStops(string? name)
        {
            if (name is not null && Media.GradientLibrary.Shared.Find(name) is { Stops.Count: >= 2 } saved)
            {
                Stops.Clear();
                foreach (var stop in saved.Stops) Stops.Add(new GradientStopViewModel(stop.Hex, stop.Location, stop.Opacity));
                return;
            }

            var stops = name switch
            {
                "B&W" => new[] { ("#000000", 0.0), ("#FFFFFF", 100.0) },
                "Sunset" => new[] { ("#2B1055", 0.0), ("#D53369", 45.0), ("#F7B733", 100.0) },
                "Chrome" => new[] { ("#3A3A3A", 0.0), ("#FFFFFF", 30.0), ("#5C5C5C", 50.0), ("#E0E0E0", 75.0), ("#2A2A2A", 100.0) },
                "Fade" => new[] { ("#000000", 0.0), ("#000000", 100.0) },
                _ => new[] { ("#FF0000", 0.0), ("#FFFF00", 17.0), ("#00FF00", 33.0), ("#00FFFF", 50.0), ("#0000FF", 67.0), ("#FF00FF", 83.0), ("#FF0000", 100.0) },
            };
            Stops.Clear();
            for (var i = 0; i < stops.Length; i++)
                Stops.Add(new GradientStopViewModel(stops[i].Item1, stops[i].Item2, name == "Fade" && i == stops.Length - 1 ? 0 : 100));
        }

        /// <summary>Adds a stop halfway along the widest gap, colored like the gradient there.</summary>
        [RelayCommand]
        private void AddStop()
        {
            var sorted = Stops.OrderBy(s => s.Location).ToList();
            double location = 50;
            if (sorted.Count >= 2)
            {
                var widest = Enumerable.Range(0, sorted.Count - 1).OrderByDescending(i => sorted[i + 1].Location - sorted[i].Location).First();
                location = (sorted[widest].Location + sorted[widest + 1].Location) / 2;
            }
            var color = ColorAt(location / 100);
            Stops.Add(new GradientStopViewModel($"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}", Math.Round(location), Math.Round(color.Alpha / 2.55)));
        }

        [RelayCommand]
        private void RemoveStop(GradientStopViewModel? stop)
        {
            if (stop is null || Stops.Count <= 2) return; // a gradient needs two stops
            Stops.Remove(stop);
        }

        /// <summary>The stops as Skia arrays, sorted by location.</summary>
        public (SKColor[] Colors, float[] Positions) StopArrays()
        {
            var sorted = Stops.OrderBy(s => s.Location).ToList();
            if (sorted.Count == 0) return (new[] { SKColors.Black, SKColors.White }, new[] { 0f, 1f });
            if (sorted.Count == 1) return (new[] { sorted[0].Color, sorted[0].Color }, new[] { 0f, 1f });
            return (sorted.Select(s => s.Color).ToArray(), sorted.Select(s => (float)Math.Clamp(s.Location / 100, 0, 1)).ToArray());
        }

        /// <summary>The same gradient running the other way.</summary>
        public static (SKColor[] Colors, float[] Positions) Reversed(SKColor[] colors, float[] positions) =>
            (colors.Reverse().ToArray(), positions.Reverse().Select(p => 1 - p).ToArray());

        /// <summary>The custom gradient's color at <paramref name="t"/> (0..1).</summary>
        public SKColor ColorAt(double t)
        {
            var (colors, positions) = StopArrays();
            if (t <= positions[0]) return colors[0];
            for (var i = 1; i < positions.Length; i++)
            {
                if (t > positions[i]) continue;
                var span = positions[i] - positions[i - 1];
                var f = span <= 0 ? 1 : (float)((t - positions[i - 1]) / span);
                byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * f);
                return new SKColor(Mix(colors[i - 1].Red, colors[i].Red), Mix(colors[i - 1].Green, colors[i].Green),
                    Mix(colors[i - 1].Blue, colors[i].Blue), Mix(colors[i - 1].Alpha, colors[i].Alpha));
            }
            return colors[^1];
        }

        private void OnStopsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
                foreach (GradientStopViewModel stop in e.OldItems) stop.PropertyChanged -= OnStopPropertyChanged;
            if (e.NewItems is not null)
                foreach (GradientStopViewModel stop in e.NewItems) stop.PropertyChanged += OnStopPropertyChanged;
            if (e.Action == NotifyCollectionChangedAction.Reset)
                foreach (var stop in Stops) { stop.PropertyChanged -= OnStopPropertyChanged; stop.PropertyChanged += OnStopPropertyChanged; }
            RefreshPreview();
        }

        private void OnStopPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshPreview();

        private void RefreshPreview()
        {
            var (colors, positions) = StopArrays();
            var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
            for (var i = 0; i < colors.Length; i++)
                brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(colors[i].Alpha, colors[i].Red, colors[i].Green, colors[i].Blue), positions[i]));
            brush.Freeze();
            PreviewBrush = brush;
        }
    }
}
