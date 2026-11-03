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

    /// <summary>A drop-down row (which output channel / color range the sliders edit).</summary>
    public sealed partial class AdjustmentChoiceField : ObservableObject
    {
        public AdjustmentChoiceField(string label, IReadOnlyList<string> options)
        {
            Label = label;
            Options = options;
        }

        public string Label { get; }

        public IReadOnlyList<string> Options { get; }

        [ObservableProperty] private int selectedIndex;
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
                AdjustmentKind.ChannelMixer => Paged(initial, "Output Channel", new[] { "Red", "Green", "Blue" },
                    new[] { ("Red", -200.0, 200.0), ("Green", -200, 200), ("Blue", -200, 200), ("Constant", -200, 200) }, "Monochrome"),
                AdjustmentKind.SelectiveColor => Paged(initial, "Colors", AdjustmentSettings.SelectiveColorRanges,
                    new[] { ("Cyan", -100.0, 100.0), ("Magenta", -100, 100), ("Yellow", -100, 100), ("Black", -100, 100) }, "Absolute"),
                AdjustmentKind.ColorLookup => new List<object>
                {
                    new AdjustmentChoiceField("Look", AdjustmentSettings.ColorLookupLooks) { SelectedIndex = (int)Math.Clamp(V(0, 0), 0, AdjustmentSettings.ColorLookupLooks.Length - 1) },
                    new AdjustmentSliderField("Intensity", 0, 100, V(1, 100), "%"),
                },
                _ => throw new ArgumentException($"{initial.Kind} has its own dialog.", nameof(initial)),
            };

            foreach (var field in Fields.OfType<INotifyPropertyChanged>())
                field.PropertyChanged += (_, _) => { if (!_loadingPage) OnPropertyChanged(nameof(Fields)); };
        }

        // ----- Paged settings: a drop-down picks which group of values the sliders show -----

        private double[]? _pages;
        private AdjustmentChoiceField? _page;
        private AdjustmentSliderField[] _pageSliders = Array.Empty<AdjustmentSliderField>();
        private AdjustmentCheckField? _pageCheck;
        private bool _loadingPage;

        private List<object> Paged(AdjustmentSettings initial, string choiceLabel, IReadOnlyList<string> pages,
            (string Label, double Min, double Max)[] sliders, string checkLabel)
        {
            var count = pages.Count * sliders.Length;
            _pages = Enumerable.Range(0, count).Select(i => i < initial.Values.Count ? initial.Values[i] : 0).ToArray();
            _page = new AdjustmentChoiceField(choiceLabel, pages);
            _pageSliders = sliders.Select((s, i) => new AdjustmentSliderField(s.Label, s.Min, s.Max, _pages[i], "%")).ToArray();
            _pageCheck = new AdjustmentCheckField(checkLabel, count < initial.Values.Count && initial.Values[count] >= 0.5);

            _page.PropertyChanged += (_, _) =>
            {
                _loadingPage = true;
                for (var i = 0; i < _pageSliders.Length; i++) _pageSliders[i].Value = _pages[_page.SelectedIndex * _pageSliders.Length + i];
                _loadingPage = false;
            };
            for (var i = 0; i < _pageSliders.Length; i++)
            {
                var index = i;
                _pageSliders[i].PropertyChanged += (_, _) =>
                {
                    if (!_loadingPage) _pages[_page.SelectedIndex * _pageSliders.Length + index] = _pageSliders[index].Value;
                };
            }

            var fields = new List<object> { _page };
            fields.AddRange(_pageSliders);
            fields.Add(_pageCheck);
            return fields;
        }

        public static bool Handles(AdjustmentKind kind) => kind is
            AdjustmentKind.Exposure or AdjustmentKind.BlackWhite or AdjustmentKind.ColorBalance or
            AdjustmentKind.PhotoFilter or AdjustmentKind.GradientMap or AdjustmentKind.ChannelMixer or
            AdjustmentKind.SelectiveColor or AdjustmentKind.ColorLookup;

        /// <summary>Switches the drop-down of Channel Mixer / Selective Color (tests use it).</summary>
        public void ShowPage(int index)
        {
            if (_page is not null) _page.SelectedIndex = index;
        }

        public IReadOnlyList<AdjustmentSliderField> Sliders => Fields.OfType<AdjustmentSliderField>().ToList();

        public AdjustmentKind Kind { get; }

        public string Title { get; }

        public IReadOnlyList<object> Fields { get; }

        public AdjustmentSettings ToSettings() => _pages is not null
            ? new(Kind, _pages.Append(_pageCheck!.IsChecked ? 1.0 : 0.0))
            : new(Kind, Fields.Select(field => field switch
        {
            AdjustmentSliderField slider => slider.Value,
            AdjustmentCheckField check => check.IsChecked ? 1.0 : 0.0,
            AdjustmentChoiceField choice => choice.SelectedIndex,
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
