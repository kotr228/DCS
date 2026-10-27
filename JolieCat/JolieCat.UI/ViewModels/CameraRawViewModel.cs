using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Filter > Camera Raw Filter: the Basic panel's sliders. Every change raises
    /// PropertyChanged, which MainViewModel turns into a live canvas preview through
    /// <see cref="Apply"/>. The slow part (blurred brightness maps) is prepared once
    /// per source image.
    /// </summary>
    public partial class CameraRawViewModel : ObservableObject
    {
        private SKBitmap? _preparedFor;
        private CameraRawFilter? _filter;

        [ObservableProperty] private double temperature;
        [ObservableProperty] private double tint;
        [ObservableProperty] private double exposure;
        [ObservableProperty] private double contrast;
        [ObservableProperty] private double highlights;
        [ObservableProperty] private double shadows;
        [ObservableProperty] private double whites;
        [ObservableProperty] private double blacks;
        [ObservableProperty] private double clarity;
        [ObservableProperty] private double vibrance;
        [ObservableProperty] private double saturation;

        public CameraRawSettings Settings => new()
        {
            Temperature = Temperature,
            Tint = Tint,
            Exposure = Exposure,
            Contrast = Contrast,
            Highlights = Highlights,
            Shadows = Shadows,
            Whites = Whites,
            Blacks = Blacks,
            Clarity = Clarity,
            Vibrance = Vibrance,
            Saturation = Saturation,
        };

        public SKBitmap Apply(SKBitmap source)
        {
            if (!ReferenceEquals(source, _preparedFor) || _filter is null)
            {
                _filter = CameraRawFilter.Prepare(source);
                _preparedFor = source;
            }
            return _filter.Render(Settings);
        }

        [RelayCommand]
        private void Reset()
        {
            Temperature = Tint = Exposure = Contrast = 0;
            Highlights = Shadows = Whites = Blacks = 0;
            Clarity = Vibrance = Saturation = 0;
        }

        /// <summary>A quick starting point, like Camera Raw's "Auto": opens shadows,
        /// tames highlights and adds a little punch.</summary>
        [RelayCommand]
        private void Auto()
        {
            Highlights = -40;
            Shadows = 35;
            Whites = 10;
            Blacks = -10;
            Clarity = 10;
            Vibrance = 20;
        }
    }
}
