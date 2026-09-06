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

        public ResizeCanvasViewModel(int currentWidth, int currentHeight)
        {
            width = Math.Max(1, currentWidth);
            height = Math.Max(1, currentHeight);
            _aspectRatio = currentHeight > 0 ? (double)currentWidth / currentHeight : 1.0;
        }

        partial void OnWidthChanged(double value)
        {
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
