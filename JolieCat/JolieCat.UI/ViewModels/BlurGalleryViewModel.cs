using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Filter > Blur Gallery (Tilt-Shift or Iris Blur): sliders in percent of the image,
    /// previewed live on the canvas. The blurred versions of the image are prepared
    /// once (and again only when the Blur amount changes).
    /// </summary>
    public partial class BlurGalleryViewModel : ObservableObject
    {
        private SKBitmap? _preparedFor;
        private BlurGallery? _gallery;

        public BlurGalleryViewModel(bool iris)
        {
            IsIris = iris;
            if (iris)
            {
                size = 20;
                transition = 15;
            }
        }

        public bool IsIris { get; }

        public bool IsTiltShift => !IsIris;

        public string Title => IsIris ? "Iris Blur" : "Tilt-Shift";

        [ObservableProperty] private double centerX = 50;
        [ObservableProperty] private double centerY = 50;

        /// <summary>Tilt-Shift: sharp band height; Iris: ellipse width (radius), % of the image.</summary>
        [ObservableProperty] private double size = 12;

        /// <summary>Iris: ellipse height (radius), % of the image.</summary>
        [ObservableProperty] private double sizeY = 25;

        [ObservableProperty] private double transition = 20;
        [ObservableProperty] private double angle;
        [ObservableProperty] private double blur = 15;
        [ObservableProperty] private double saturation;

        public BlurGallerySettings Settings => new()
        {
            Iris = IsIris,
            CenterX = CenterX / 100,
            CenterY = CenterY / 100,
            Size = Size / 100,
            SizeY = SizeY / 100,
            Transition = Transition / 100,
            Angle = Angle,
            Blur = Blur,
            Saturation = Saturation,
        };

        public SKBitmap Apply(SKBitmap source)
        {
            if (_gallery is null || !ReferenceEquals(source, _preparedFor) || _gallery.PreparedBlur != (float)Blur)
            {
                _gallery = BlurGallery.Prepare(source, Blur);
                _preparedFor = source;
            }
            return _gallery.Render(Settings);
        }
    }
}
