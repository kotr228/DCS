using System;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;
using JolieCat.UI.Media;
using System.Windows.Media.Imaging;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>
    /// Bindable wrapper around a single <see cref="Core.Documents.Layer"/> for the Layers
    /// panel: <see cref="Core.Documents.Layer"/> itself is a plain Core domain object (no
    /// INotifyPropertyChanged), so this is what the ListBox's Name text, visibility/lock
    /// toggles, blend mode, and opacity slider actually bind to. Every property reads/
    /// writes straight through to <see cref="Model"/> - this class holds no state of its
    /// own beyond <see cref="IsEditingName"/>, which is purely this panel's inline-rename
    /// UI state and has no Core equivalent.
    /// </summary>
    public partial class LayerViewModel : ObservableObject
    {
        /// <summary>The underlying Core layer - also where its <see cref="Layer.Bitmap"/> lives.</summary>
        public Layer Model { get; }

        /// <summary>Raised whenever a change here should trigger a canvas repaint (visibility,
        /// opacity, or blend mode - anything that affects composited pixels, not just the name).</summary>
        public event EventHandler? VisualStateChanged;

        /// <summary>True while the Layers panel is showing this layer's inline rename
        /// textbox instead of its plain name label - set by the view's double-click
        /// handler, not by anything in Core.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotEditingName))]
        private bool isEditingName;

        /// <summary>Inverse of <see cref="IsEditingName"/>, for the plain name label's own
        /// Visibility binding - <see cref="System.Windows.Data.IValueConverter"/>s in this
        /// app take no parameters, so a real property is simpler than adding one just to
        /// invert a bool.</summary>
        public bool IsNotEditingName => !IsEditingName;

        /// <summary>The mask thumbnail shown in the Layers panel row, or null when this
        /// layer has no mask - refreshed via <see cref="RefreshMaskThumbnail"/> after any
        /// mask edit, add, or remove.</summary>
        [ObservableProperty]
        private BitmapSource? maskThumbnail;

        public LayerViewModel(Layer model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            RefreshMaskThumbnail();
        }

        /// <summary>Whether this layer currently has a mask attached at all - the
        /// Layers panel row shows either the mask thumbnail (see
        /// <see cref="MaskThumbnail"/>) or a plain "add mask" affordance based on this.</summary>
        public bool HasMask => Model.Mask is not null;

        /// <summary>Whether an existing mask currently affects compositing - mirrors
        /// <see cref="LayerMask.IsEnabled"/>; meaningless (and left false) while
        /// <see cref="HasMask"/> is false.</summary>
        public bool IsMaskEnabled
        {
            get => Model.Mask?.IsEnabled ?? false;
            set
            {
                if (Model.Mask is not { } mask || mask.IsEnabled == value) return;
                mask.IsEnabled = value;
                OnPropertyChanged();
                VisualStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>True while this layer's mask - not its own color content - is what
        /// painting tools draw onto (see <see cref="Layer.IsMaskActive"/>). Selected by
        /// clicking the mask thumbnail in the Layers panel, the same gesture every other
        /// raster editor uses to switch a layer's paint target to its mask.</summary>
        public bool IsMaskActive
        {
            get => Model.IsMaskActive;
            set
            {
                if (Model.IsMaskActive == value) return;
                Model.IsMaskActive = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Recomputes <see cref="MaskThumbnail"/> from the mask's current
        /// pixels (null if there is no mask), and re-announces <see cref="HasMask"/>/
        /// <see cref="IsMaskEnabled"/> - both plain computed reads of <see cref="Model"/>,
        /// so unlike <see cref="MaskThumbnail"/> itself they have no setter of their own
        /// to raise a change from. Called after this layer's mask is added, removed, or
        /// painted on - whichever of the three actually changed, calling this
        /// unconditionally is simpler and cheaper than tracking which.</summary>
        public void RefreshMaskThumbnail()
        {
            MaskThumbnail = Model.Mask is { } mask ? SkiaImageConverter.ToThumbnail(mask.Bitmap) : null;
            OnPropertyChanged(nameof(HasMask));
            OnPropertyChanged(nameof(IsMaskEnabled));
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

        /// <summary>Compositing mode used when this layer is drawn over the ones beneath
        /// it - the Layers panel's Blend Mode dropdown.</summary>
        public BlendMode BlendMode
        {
            get => Model.BlendMode;
            set
            {
                if (Model.BlendMode == value) return;
                Model.BlendMode = value;
                OnPropertyChanged();
                VisualStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
