using System;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Documents;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>
    /// Bindable wrapper around a single <see cref="Core.Documents.Layer"/> for the Layers
    /// panel: <see cref="Core.Documents.Layer"/> itself is a plain Core domain object (no
    /// INotifyPropertyChanged), so this is what the ListBox's Name text, visibility/lock
    /// toggles, and opacity slider actually bind to. Every property reads/writes straight
    /// through to <see cref="Model"/> - this class holds no state of its own.
    /// </summary>
    public partial class LayerViewModel : ObservableObject
    {
        /// <summary>The underlying Core layer - also where its <see cref="Layer.Bitmap"/> lives.</summary>
        public Layer Model { get; }

        /// <summary>Raised whenever a change here should trigger a canvas repaint (visibility,
        /// opacity, or blend mode - anything that affects composited pixels, not just the name).</summary>
        public event EventHandler? VisualStateChanged;

        public LayerViewModel(Layer model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public string Name
        {
            get => Model.Name;
            set
            {
                if (Model.Name == value) return;
                Model.Name = value;
                OnPropertyChanged();
            }
        }

        public bool IsVisible
        {
            get => Model.IsVisible;
            set
            {
                if (Model.IsVisible == value) return;
                Model.IsVisible = value;
                OnPropertyChanged();
                VisualStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsLocked
        {
            get => Model.IsLocked;
            set
            {
                if (Model.IsLocked == value) return;
                Model.IsLocked = value;
                OnPropertyChanged();
            }
        }

        /// <summary>0-100, for the Layers panel's opacity slider - <see cref="Layer.Opacity"/>
        /// itself stays the canonical 0.0-1.0 range everywhere else.</summary>
        public double OpacityPercent
        {
            get => Model.Opacity * 100.0;
            set
            {
                var clamped = Math.Clamp(value, 0.0, 100.0) / 100.0;
                if (Math.Abs(Model.Opacity - clamped) < 0.0001) return;
                Model.Opacity = clamped;
                OnPropertyChanged();
                VisualStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
