using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JolieCat.UI.Media;

namespace JolieCat.UI.Controls
{
    /// <summary>
    /// A Photoshop-style Saturation/Brightness square plus a Hue strip - the "professional"
    /// replacement for plain RGB sliders in <see cref="Views.ColorPickerPanel"/>. Owns no
    /// color state of its own: <see cref="Hue"/>, <see cref="Saturation"/>, and
    /// <see cref="Brightness"/> are plain bindable dependency properties, so the actual
    /// color lives wherever this is bound from (<see cref="ViewModels.CanvasViewModel"/>,
    /// which keeps the same three values and converts them to/from its RGB PrimaryColor).
    /// </summary>
    public partial class HsvColorPicker : UserControl
    {
        public static readonly DependencyProperty HueProperty = DependencyProperty.Register(
            nameof(Hue), typeof(double), typeof(HsvColorPicker),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHsvChanged));

        public static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(
            nameof(Saturation), typeof(double), typeof(HsvColorPicker),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHsvChanged));

        public static readonly DependencyProperty BrightnessProperty = DependencyProperty.Register(
            nameof(Brightness), typeof(double), typeof(HsvColorPicker),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHsvChanged));

        public double Hue
        {
            get => (double)GetValue(HueProperty);
            set => SetValue(HueProperty, value);
        }

        public double Saturation
        {
            get => (double)GetValue(SaturationProperty);
            set => SetValue(SaturationProperty, value);
        }

        public double Brightness
        {
            get => (double)GetValue(BrightnessProperty);
            set => SetValue(BrightnessProperty, value);
        }

        public HsvColorPicker()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateVisuals();
            SizeChanged += (_, _) => UpdateVisuals();
        }

        private static void OnHsvChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((HsvColorPicker)d).UpdateVisuals();

        /// <summary>Repaints the SV box's hue-tinted base layer and repositions both
        /// thumbs to match the current Hue/Saturation/Brightness. Safe to call before
        /// the control has laid out (guards on both the named elements existing and
        /// having a real size).</summary>
        private void UpdateVisuals()
        {
            if (HueLayer is null) return;

            var (r, g, b) = HsvColor.ToRgb(Hue, 1.0, 1.0);
            HueLayer.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));

            var svWidth = SvBox.ActualWidth;
            var svHeight = SvBox.ActualHeight;
            if (svWidth > 0 && svHeight > 0)
            {
                Canvas.SetLeft(SvThumb, Saturation * svWidth - SvThumb.Width / 2);
                Canvas.SetTop(SvThumb, (1 - Brightness) * svHeight - SvThumb.Height / 2);
            }

            var hueWidth = HueSlider.ActualWidth;
            if (hueWidth > 0)
                Canvas.SetLeft(HueThumb, Hue / 360.0 * hueWidth - HueThumb.Width / 2);
        }

        private void SvBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SvBox.CaptureMouse();
            UpdateSvFromPoint(e.GetPosition(SvBox));
        }

        private void SvBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && SvBox.IsMouseCaptured)
                UpdateSvFromPoint(e.GetPosition(SvBox));
        }

        private void SvBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => SvBox.ReleaseMouseCapture();

        private void UpdateSvFromPoint(Point point)
        {
            var width = SvBox.ActualWidth;
            var height = SvBox.ActualHeight;
            if (width <= 0 || height <= 0) return;

            Saturation = Math.Clamp(point.X / width, 0.0, 1.0);
            Brightness = Math.Clamp(1.0 - point.Y / height, 0.0, 1.0);
        }

        private void HueSlider_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            HueSlider.CaptureMouse();
            UpdateHueFromPoint(e.GetPosition(HueSlider));
        }

        private void HueSlider_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && HueSlider.IsMouseCaptured)
                UpdateHueFromPoint(e.GetPosition(HueSlider));
        }

        private void HueSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => HueSlider.ReleaseMouseCapture();

        private void UpdateHueFromPoint(Point point)
        {
            var width = HueSlider.ActualWidth;
            if (width <= 0) return;

            Hue = Math.Clamp(point.X / width, 0.0, 1.0) * 360.0;
        }
    }
}
