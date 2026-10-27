using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        // ================= Rotate View =================
        //
        // A view-only rotation (the image itself is untouched), applied around the
        // viewport's center on top of pan and zoom: screen = Rotate(doc * zoom + pan).
        // Pan/zoom math therefore keeps working in "unrotated screen" space; a pointer
        // position is simply rotated back first (see ToUnrotatedScreen).

        /// <summary>View rotation in degrees, clockwise, -180..180.</summary>
        [ObservableProperty]
        private double viewRotation;

        public bool IsViewRotated => Math.Abs(ViewRotation) > 0.01;

        partial void OnViewRotationChanged(double value)
        {
            OnPropertyChanged(nameof(IsViewRotated));
            OnPropertyChanged(nameof(TextEditScreenX));
            OnPropertyChanged(nameof(TextEditScreenY));
            RaiseInvalidate();
        }

        private SKPoint ViewportCenter => new((float)(_viewportWidth / 2), (float)(_viewportHeight / 2));

        /// <summary>Screen point → where it would be with no view rotation.</summary>
        internal SKPoint ToUnrotatedScreen(SKPoint screen)
        {
            if (!IsViewRotated) return screen;
            return SKMatrix.CreateRotationDegrees((float)-ViewRotation, ViewportCenter.X, ViewportCenter.Y).MapPoint(screen);
        }

        /// <summary>Unrotated screen point → actual screen point.</summary>
        internal SKPoint ToRotatedScreen(SKPoint unrotated)
        {
            if (!IsViewRotated) return unrotated;
            return SKMatrix.CreateRotationDegrees((float)ViewRotation, ViewportCenter.X, ViewportCenter.Y).MapPoint(unrotated);
        }

        /// <summary>Document → screen, including the rotation (what the renderer applies).</summary>
        public SKMatrix ViewMatrix
        {
            get
            {
                var matrix = SKMatrix.CreateScale((float)Zoom, (float)Zoom)
                    .PostConcat(SKMatrix.CreateTranslation((float)PanX, (float)PanY));
                return IsViewRotated
                    ? matrix.PostConcat(SKMatrix.CreateRotationDegrees((float)ViewRotation, ViewportCenter.X, ViewportCenter.Y))
                    : matrix;
            }
        }

        /// <summary>View &gt; Reset Rotation (Esc with the Rotate View tool).</summary>
        [RelayCommand]
        public void ResetViewRotation() => ViewRotation = 0;

        private bool _isRotatingView;
        private double _rotateStartAngle;
        private double _rotateStartRotation;

        private void BeginRotateView(SKPoint devicePoint)
        {
            _isRotatingView = true;
            _rotateStartAngle = AngleFromCenter(devicePoint);
            _rotateStartRotation = ViewRotation;
        }

        /// <summary>Drag around the center to turn the view; Shift snaps to 15° steps.</summary>
        private void UpdateRotateView(SKPoint devicePoint)
        {
            var angle = _rotateStartRotation + AngleFromCenter(devicePoint) - _rotateStartAngle;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                angle = Math.Round(angle / 15) * 15;

            angle = ((angle + 180) % 360 + 360) % 360 - 180; // keep in -180..180
            ViewRotation = Math.Abs(angle) < 0.01 ? 0 : angle;
        }

        private double AngleFromCenter(SKPoint devicePoint)
        {
            var center = ViewportCenter;
            return Math.Atan2(devicePoint.Y - center.Y, devicePoint.X - center.X) * 180 / Math.PI;
        }
    }
}
