using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels
{
    /// <summary>A field of a <see cref="FieldsDialogViewModel"/>.</summary>
    public abstract partial class DialogField : ObservableObject
    {
        protected DialogField(string label) => Label = label;

        public string Label { get; }
    }

    public sealed partial class SliderDialogField : DialogField
    {
        public SliderDialogField(string label, double min, double max, double value, string suffix = "") : base(label)
        {
            Minimum = min;
            Maximum = max;
            Suffix = suffix;
            this.value = value;
        }

        public double Minimum { get; }
        public double Maximum { get; }
        public string Suffix { get; }

        [ObservableProperty] private double value;
    }

    public sealed partial class ColorDialogField : DialogField
    {
        public ColorDialogField(string label, string hex) : base(label) => this.hex = hex;

        [ObservableProperty] private string hex;
    }

    public sealed partial class ChoiceDialogField : DialogField
    {
        public ChoiceDialogField(string label, IReadOnlyList<string> options, string? selected = null) : base(label)
        {
            Options = options;
            this.selected = selected ?? (options.Count > 0 ? options[0] : string.Empty);
        }

        public IReadOnlyList<string> Options { get; }

        [ObservableProperty] private string selected;
    }

    public sealed partial class TextDialogField : DialogField
    {
        public TextDialogField(string label, string text) : base(label) => this.text = text;

        [ObservableProperty] private string text;
    }

    /// <summary>
    /// A small settings dialog built from a list of fields (sliders, colors, choices,
    /// text) - Edit > Stroke, Edit > Fade, Save/Load Selection and the like. Any change
    /// raises <see cref="Changed"/>, for live previews. <see cref="Validate"/> can keep
    /// OK from closing with a message.
    /// </summary>
    public partial class FieldsDialogViewModel : ObservableObject
    {
        public FieldsDialogViewModel(string title, string? hint = null)
        {
            Title = title;
            Hint = hint ?? string.Empty;
            Fields.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is null) return;
                foreach (INotifyPropertyChanged field in e.NewItems)
                    field.PropertyChanged += (_, _) => Changed?.Invoke();
            };
        }

        public string Title { get; }

        public string Hint { get; }

        public ObservableCollection<DialogField> Fields { get; } = new();

        [ObservableProperty]
        private string error = string.Empty;

        public event Action? Changed;

        /// <summary>Returns a message to keep the dialog open, or null to accept.</summary>
        public Func<string?>? Validate { get; set; }

        public T Add<T>(T field) where T : DialogField
        {
            Fields.Add(field);
            return field;
        }
    }
}
