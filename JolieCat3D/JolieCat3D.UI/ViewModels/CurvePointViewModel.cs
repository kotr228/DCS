using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.UI.ViewModels
{
    /// <summary>
    /// Wraps one entry of a <see cref="CurveData.Points"/> list for the Curve
    /// Inspector's own points panel - the same "rebuilt wholesale by the owning
    /// <see cref="NodeViewModel"/>, not incrementally patched" convention
    /// <see cref="MaterialSlotViewModel"/> already uses. Position and both handles are
    /// exposed one field per axis (no <see cref="Vector3"/> converter needed), the same
    /// convention <see cref="ArrayModifierViewModel"/>'s own RelativeOffsetX/Y/Z already
    /// established. <paramref name="onChanged"/> (passed by
    /// <see cref="NodeViewModel.RefreshCurvePoints"/>) is expected to both regenerate
    /// <see cref="Node.Mesh"/> from the owning <see cref="CurveData"/> AND re-render the
    /// viewport - every edit here changes the curve's own shape, so every edit needs
    /// both.
    /// </summary>
    public sealed class CurvePointViewModel : ObservableObject
    {
        private readonly CurvePoint _point;
        private readonly Action _onChanged;

        /// <summary>The wrapped point itself - <see cref="NodeViewModel.RemoveCurvePoint"/>
        /// needs this to remove the right entry from <see cref="CurveData.Points"/>, the
        /// same role <see cref="ModifierViewModelBase.Underlying"/> plays for a
        /// modifier.</summary>
        public CurvePoint Underlying => _point;

        public CurvePointViewModel(CurvePoint point, Action onChanged)
        {
            _point = point ?? throw new ArgumentNullException(nameof(point));
            _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        }

        public float PositionX
        {
            get => _point.Position.X;
            set { _point.Position = new Vector3(value, _point.Position.Y, _point.Position.Z); OnPropertyChanged(); _onChanged(); }
        }

        public float PositionY
        {
            get => _point.Position.Y;
            set { _point.Position = new Vector3(_point.Position.X, value, _point.Position.Z); OnPropertyChanged(); _onChanged(); }
        }

        public float PositionZ
        {
            get => _point.Position.Z;
            set { _point.Position = new Vector3(_point.Position.X, _point.Position.Y, value); OnPropertyChanged(); _onChanged(); }
        }

        public float HandleInX
        {
            get => _point.HandleIn.X;
            set { _point.HandleIn = new Vector3(value, _point.HandleIn.Y, _point.HandleIn.Z); OnPropertyChanged(); _onChanged(); }
        }

        public float HandleInY
        {
            get => _point.HandleIn.Y;
            set { _point.HandleIn = new Vector3(_point.HandleIn.X, value, _point.HandleIn.Z); OnPropertyChanged(); _onChanged(); }
        }

        public float HandleInZ
        {
            get => _point.HandleIn.Z;
            set { _point.HandleIn = new Vector3(_point.HandleIn.X, _point.HandleIn.Y, value); OnPropertyChanged(); _onChanged(); }
        }

        public float HandleOutX
        {
            get => _point.HandleOut.X;
            set { _point.HandleOut = new Vector3(value, _point.HandleOut.Y, _point.HandleOut.Z); OnPropertyChanged(); _onChanged(); }
        }

        public float HandleOutY
        {
            get => _point.HandleOut.Y;
            set { _point.HandleOut = new Vector3(_point.HandleOut.X, value, _point.HandleOut.Z); OnPropertyChanged(); _onChanged(); }
        }

        public float HandleOutZ
        {
            get => _point.HandleOut.Z;
            set { _point.HandleOut = new Vector3(_point.HandleOut.X, _point.HandleOut.Y, value); OnPropertyChanged(); _onChanged(); }
        }
    }
}
