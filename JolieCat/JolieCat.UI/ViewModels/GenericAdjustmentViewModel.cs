using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Adjustments;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>A slider row in <see cref="GenericAdjustmentViewModel"/>.</summary>
    public sealed partial class AdjustmentSliderField : ObservableObject
    {
        public AdjustmentSliderField(string label, double min, double max, double value, string suffix = "")
        {
            Label = label;
            Minimum = min;
            Maximum = max;
            Suffix = suffix;
            this.value = value;
        }

        public string Label { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public string Suffix { get; }

        [ObservableProperty] private double value;
    }

    /// <summary>A checkbox row.</summary>
    public sealed partial class AdjustmentCheckField : ObservableObject
    {
        public AdjustmentCheckField(string label, bool isChecked)
        {
            Label = label;
            this.isChecked = isChecked;
        }

        public string Label { get; }

        [ObservableProperty] private bool isChecked;
    }

    /// <summary>A color row, edited as #RRGGBB.</summary>
    public sealed partial class AdjustmentColorField : ObservableObject
    {
        public AdjustmentColorField(string label, SKColor color)
        {
            Label = label;
            hex = LayerStyleViewModel.Hex(color);
        }

        public string Label { get; }

        [ObservableProperty] private string hex;

        public SKColor Color => LayerStyleViewModel.Parse(Hex, SKColors.Black);
    }

    /// <summary>
    /// Settings dialog for the adjustments that are just a few numbers and colors:
    /// Exposure, Black &amp; White, Color Balance, Photo Filter and Gradient Map. The
    /// fields appear in the same order as <see cref="AdjustmentSettings.Values"/>.
    /// Any change raises PropertyChanged (for the live preview).
    /// </summary>
    public sealed class GenericAdjustmentViewModel : ObservableObject
    {
        public GenericAdjustmentViewModel(AdjustmentSettings initial)
        {
            Kind = initial.Kind;
            Title = initial.DisplayName;
            double V(int i, double fallback) => i < initial.Values.Count ? initial.Values[i] : fallback;

            Fields = initial.Kind switch
            {
                AdjustmentKind.Exposure => new List<object>
                {
                    new AdjustmentSliderField("Exposure", -5, 5, V(0, 0)),
                    new AdjustmentSliderField("Offset", -0.5, 0.5, V(1, 0)),
                    new AdjustmentSliderField("Gamma", 0.1, 3, V(2, 1)),
                },
                AdjustmentKind.BlackWhite => new List<object>
                {
                    new AdjustmentSliderField("Reds", -200, 300, V(0, 40), "%"),
                    new AdjustmentSliderField("Greens", -200, 300, V(1, 40), "%"),
                    new AdjustmentSliderField("Blues", -200, 300, V(2, 20), "%"),
                },
                AdjustmentKind.ColorBalance => new List<object>
                {
                    new AdjustmentSliderField("Shadows: Cyan - Red", -100, 100, V(0, 0)),
                    new AdjustmentSliderField("Shadows: Magenta - Green", -100, 100, V(1, 0)),
                    new AdjustmentSliderField("Shadows: Yellow - Blue", -100, 100, V(2, 0)),
                    new AdjustmentSliderField("Midtones: Cyan - Red", -100, 100, V(3, 0)),
                    new AdjustmentSliderField("Midtones: Magenta - Green", -100, 100, V(4, 0)),
                    new AdjustmentSliderField("Midtones: Yellow - Blue", -100, 100, V(5, 0)),
                    new AdjustmentSliderField("Highlights: Cyan - Red", -100, 100, V(6, 0)),
                    new AdjustmentSliderField("Highlights: Magenta - Green", -100, 100, V(7, 0)),
                    new AdjustmentSliderField("Highlights: Yellow - Blue", -100, 100, V(8, 0)),
                },
                AdjustmentKind.PhotoFilter => new List<object>
                {
                    new AdjustmentSliderField("Density", 0, 100, V(0, 25), "%"),
                    new AdjustmentCheckField("Preserve Luminosity", V(1, 1) >= 0.5),
                    new AdjustmentColorField("Filter color", AdjustmentSettings.UnpackColor(V(2, 0xEC8A00))),
                },
                AdjustmentKind.GradientMap => new List<object>
                {
                    new AdjustmentColorField("Shadows", AdjustmentSettings.UnpackColor(V(0, 0x000000))),
                    new AdjustmentColorField("Highlights", AdjustmentSettings.UnpackColor(V(1, 0xFFFFFF))),
                },
                _ => throw new ArgumentException($"{initial.Kind} has its own dialog.", nameof(initial)),
            };

            foreach (var field in Fields.OfType<INotifyPropertyChanged>())
                field.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Fields));
        }

        public static bool Handles(AdjustmentKind kind) => kind is
            AdjustmentKind.Exposure or AdjustmentKind.BlackWhite or AdjustmentKind.ColorBalance or
            AdjustmentKind.PhotoFilter or AdjustmentKind.GradientMap;

        public AdjustmentKind Kind { get; }

        public string Title { get; }

        public IReadOnlyList<object> Fields { get; }

        public AdjustmentSettings ToSettings() => new(Kind, Fields.Select(field => field switch
        {
            AdjustmentSliderField slider => slider.Value,
            AdjustmentCheckField check => check.IsChecked ? 1.0 : 0.0,
            AdjustmentColorField color => AdjustmentSettings.PackColor(color.Color),
            _ => 0.0,
        }));

        /// <summary>The destructive version: <paramref name="source"/> run through the
        /// adjustment, as a new bitmap.</summary>
        public SKBitmap Apply(SKBitmap source)
        {
            var result = new SKBitmap(source.Info);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            using var filter = ToSettings().CreateColorFilter();
            using var paint = new SKPaint { ColorFilter = filter };
            canvas.DrawBitmap(source, 0, 0, paint);
            return result;
        }
    }
}
