using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JolieCatEngine.Scripting.CSharp
{
    public class TransformComponent : INotifyPropertyChanged
    {
        private float _positionX;
        private float _positionY;
        private float _positionZ;
        private float _rotationX;
        private float _rotationY;
        private float _rotationZ;
        private float _scaleX = 1f;
        private float _scaleY = 1f;
        private float _scaleZ = 1f;

        public uint NativeId { get; set; }

        public float PositionX { get => _positionX; set => SetField(ref _positionX, value); }
        public float PositionY { get => _positionY; set => SetField(ref _positionY, value); }
        public float PositionZ { get => _positionZ; set => SetField(ref _positionZ, value); }

        public float RotationX { get => _rotationX; set => SetField(ref _rotationX, value); }
        public float RotationY { get => _rotationY; set => SetField(ref _rotationY, value); }
        public float RotationZ { get => _rotationZ; set => SetField(ref _rotationZ, value); }

        public float ScaleX { get => _scaleX; set => SetField(ref _scaleX, value); }
        public float ScaleY { get => _scaleY; set => SetField(ref _scaleY, value); }
        public float ScaleZ { get => _scaleZ; set => SetField(ref _scaleZ, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                SyncToNative();
            }
        }

        private void SyncToNative()
        {
            NativeBridge.Engine_SetTransform(
                NativeId,
                _positionX, _positionY, _positionZ,
                _rotationX, _rotationY, _rotationZ,
                _scaleX, _scaleY, _scaleZ);

            DebugConsole.Log($"[Bridge] Synced Entity {NativeId} Transform to Core");
        }
    }
}
