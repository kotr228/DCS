using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// The "Resize Canvas..." dialog's own state: the document's current width/height,
    /// editable, with an optional aspect-ratio lock - the same two-way "changing one
    /// property pushes the other" sync <see cref="CanvasViewModel"/>'s Hue/Saturation/
    /// Brightness already use for <c>PrimaryColor</c>, guarded here by
    /// <see cref="_isSyncing"/> instead of that class's own <c>_isSyncingColor</c>.
    /// Applying the result is the caller's job (<c>MainViewModel.ResizeCanvas</c> reads
    /// <see cref="Width"/>/<see cref="Height"/> once <see cref="System.Windows.Window.DialogResult"/>
    /// is true and calls <c>LayersViewModel.ResizeDocument</c>) - this class only holds
    /// and validates the two numbers, exactly like every other options view model in
    /// this app that a dialog's Apply/Cancel buttons read from rather than call into.
    /// </summary>
    public partial class ResizeCanvasViewModel : ObservableObject
    {
        private readonly double _aspectRatio;
        private bool _isSyncing;

        [ObservableProperty]
        private double width;

        [ObservableProperty]
        private double height;

        /// <summary>When true, changing either <see cref="Width"/> or <see cref="Height"/>
        /// pushes the other to preserve the aspect ratio the dialog opened with -
        /// "optional aspect ratio locking" the same way the Crop tool's own
        /// <c>CropToolOptionsViewModel.AspectRatio</c> lock works, just as a plain
        /// on/off toggle rather than a set of named ratios (there's no single "original"
        /// ratio to offer as a preset here beyond the one already showing).</summary>
        [ObservableProperty]
        private bool lockAspectRatio = true;

        /// <summary>True for Image Size (content is rescaled); false for Canvas Size
        /// (content keeps its pixel size and is cropped or padded).</summary>
        public bool IsResample { get; }

        public bool IsCanvasSize => !IsResample;

        public string Title => IsResample ? "Image Size" : "Canvas Size";

        public string ApplyLabel => IsResample ? "Resize" : "Apply";

        public string Description => IsResample
            ? "Every layer and mask is rescaled to the new size. Lock the aspect ratio to avoid stretching."
            : "Content keeps its size: cropped if you shrink, padded with transparency if you grow.";

        /// <summary>Canvas Size only: keep the content centered (Photoshop's default
        /// anchor) instead of pinned to the top-left corner.</summary>
        [ObservableProperty]
        private bool anchorCenter = true;

        [ObservableProperty]
        private double percent = 100;

        private readonly int _originalWidth;

        public ResizeCanvasViewModel(int currentWidth, int currentHeight, bool resample = false)
        {
            IsResample = resample;
            _originalWidth = Math.Max(1, currentWidth);
            width = Math.Max(1, currentWidth);
            height = Math.Max(1, currentHeight);
            _aspectRatio = currentHeight > 0 ? (double)currentWidth / currentHeight : 1.0;
        }

        /// <summary>Image Size shortcut: a percentage of the original size (both sides).</summary>
        partial void OnPercentChanged(double value)
        {
            if (_isSyncing || value <= 0) return;

            var wasLocked = LockAspectRatio;
            LockAspectRatio = true;
            Width = Math.Max(1, Math.Round(_originalWidth * value / 100.0));
            LockAspectRatio = wasLocked;
        }

        partial void OnWidthChanged(double value)
        {
            percent = Math.Round(value * 100.0 / _originalWidth, 1);
            OnPropertyChanged(nameof(Percent));

            if (_isSyncing || !LockAspectRatio || _aspectRatio <= 0) return;

            _isSyncing = true;
            Height = Math.Max(1, Math.Round(value / _aspectRatio));
            _isSyncing = false;
        }

        partial void OnHeightChanged(double value)
        {
            if (_isSyncing || !LockAspectRatio) return;

            _isSyncing = true;
            Width = Math.Max(1, Math.Round(value * _aspectRatio));
            _isSyncing = false;
        }
    }
}
