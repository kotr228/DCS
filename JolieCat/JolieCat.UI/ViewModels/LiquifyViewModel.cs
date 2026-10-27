using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public enum LiquifyTool
    {
        ForwardWarp,
        Reconstruct,
        Pucker,
        Bloat,
        TwirlClockwise,
        TwirlCounterClockwise,
    }

    /// <summary>
    /// The Liquify dialog: brushes that push, pinch, bloat and twirl the layer's pixels.
    /// Strokes edit a <see cref="LiquifyField"/>; the dialog shows a scaled-down live
    /// preview, and <see cref="ApplyTo"/> renders the full-size result once on OK.
    /// Positions passed in are preview-image pixels.
    /// </summary>
    public partial class LiquifyViewModel : ObservableObject, IDisposable
    {
        private readonly int _imageWidth;
        private readonly int _imageHeight;
        private readonly SKBitmap _previewSource;
        private readonly SKBitmap? _previewBackdrop;
        private readonly Stack<LiquifyField> _undo = new();
        private bool _stroking;
        private SKPoint _last;

        /// <param name="backdrop">Optional: the rest of the image (the other layers), shown
        /// under the layer while it is reshaped. Same size as <paramref name="source"/>.</param>
        public LiquifyViewModel(SKBitmap source, int maxPreviewWidth = 900, int maxPreviewHeight = 640, SKBitmap? backdrop = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            _imageWidth = source.Width;
            _imageHeight = source.Height;

            PreviewScale = Math.Min(1.0, Math.Min((double)maxPreviewWidth / source.Width, (double)maxPreviewHeight / source.Height));
            PreviewWidth = Math.Max(1, (int)Math.Round(source.Width * PreviewScale));
            PreviewHeight = Math.Max(1, (int)Math.Round(source.Height * PreviewScale));
            _previewSource = PreviewScale < 1
                ? source.Resize(new SKImageInfo(PreviewWidth, PreviewHeight, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                : source.Copy(SKColorType.Rgba8888);

            if (backdrop is not null)
                _previewBackdrop = backdrop.Resize(new SKImageInfo(PreviewWidth, PreviewHeight, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            showBackdrop = _previewBackdrop is not null;

            Field = LiquifyField.ForImage(source.Width, source.Height);
            Preview = new WriteableBitmap(PreviewWidth, PreviewHeight, 96, 96, PixelFormats.Pbgra32, null);
            RenderPreview();
        }

        public LiquifyField Field { get; }

        /// <summary>Preview pixels per image pixel (at most 1).</summary>
        public double PreviewScale { get; }

        public int PreviewWidth { get; }

        public int PreviewHeight { get; }

        public WriteableBitmap Preview { get; }

        [ObservableProperty]
        private LiquifyTool tool = LiquifyTool.ForwardWarp;

        /// <summary>Brush diameter in image pixels.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BrushPreviewDiameter))]
        private double brushSize = 120;

        /// <summary>0..100 %.</summary>
        [ObservableProperty]
        private double pressure = 50;

        /// <summary>Whether the other layers are drawn under the layer being reshaped.</summary>
        [ObservableProperty]
        private bool showBackdrop;

        public bool HasBackdrop => _previewBackdrop is not null;

        partial void OnShowBackdropChanged(bool value) => RenderPreview();

        /// <summary>The brush circle's size on the preview.</summary>
        public double BrushPreviewDiameter => BrushSize * PreviewScale;

        public bool HasChanges => !Field.IsIdentity;

        public bool CanUndo => _undo.Count > 0;

        // Field pixels per image pixel.
        private float FieldScale => (float)Field.Width / _imageWidth;

        private SKPoint ToField(double previewX, double previewY)
        {
            var s = FieldScale / PreviewScale;
            return new SKPoint((float)(previewX * s) - 0.5f, (float)(previewY * s) - 0.5f);
        }

        private float FieldRadius => (float)(BrushSize / 2 * FieldScale);

        private float Strength => (float)(Pressure / 50.0);

        // ----- Strokes -----

        public void BeginStroke(double previewX, double previewY)
        {
            _undo.Push(Field.Clone());
            OnPropertyChanged(nameof(CanUndo));
            _stroking = true;
            _last = ToField(previewX, previewY);
            if (Tool != LiquifyTool.ForwardWarp) Dab(_last);
            RenderPreview();
        }

        public void ContinueStroke(double previewX, double previewY)
        {
            if (!_stroking) return;
            var point = ToField(previewX, previewY);

            if (Tool == LiquifyTool.ForwardWarp)
            {
                // Long moves are split up so the push follows the path smoothly.
                var move = point - _last;
                var length = move.Length;
                if (length < 0.01f) return;
                var steps = Math.Max(1, (int)Math.Ceiling(length / Math.Max(1, FieldRadius / 4)));
                var step = new SKPoint(move.X / steps, move.Y / steps);
                var pressure = (float)Math.Clamp(Pressure / 100.0, 0, 1);
                for (var i = 0; i < steps; i++)
                {
                    Field.Push(_last.X, _last.Y, FieldRadius, step.X, step.Y, pressure);
                    _last += step;
                }
            }
            else
            {
                // Stationary tools work wherever the brush passes (and keep working
                // while it's held still - see Tick).
                _last = point;
                Dab(point);
            }

            RenderPreview();
        }

        /// <summary>Called repeatedly while the mouse button is held, so Pucker, Bloat,
        /// Twirl and Reconstruct keep working with the brush held still.</summary>
        public void Tick()
        {
            if (!_stroking || Tool == LiquifyTool.ForwardWarp) return;
            Dab(_last);
            RenderPreview();
        }

        public void EndStroke()
        {
            if (!_stroking) return;
            _stroking = false;
            OnPropertyChanged(nameof(HasChanges));
        }

        private void Dab(SKPoint at)
        {
            var strength = Strength;
            switch (Tool)
            {
                case LiquifyTool.Pucker: Field.Pinch(at.X, at.Y, FieldRadius, 0.04f * strength); break;
                case LiquifyTool.Bloat: Field.Pinch(at.X, at.Y, FieldRadius, -0.04f * strength); break;
                case LiquifyTool.TwirlClockwise: Field.Twirl(at.X, at.Y, FieldRadius, 0.06f * strength); break;
                case LiquifyTool.TwirlCounterClockwise: Field.Twirl(at.X, at.Y, FieldRadius, -0.06f * strength); break;
                case LiquifyTool.Reconstruct: Field.Reconstruct(at.X, at.Y, FieldRadius, 0.15f * strength); break;
            }
        }

        // ----- Commands -----

        [RelayCommand]
        private void UndoStroke()
        {
            if (_undo.Count == 0) return;
            Field.CopyFrom(_undo.Pop());
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(HasChanges));
            RenderPreview();
        }

        [RelayCommand]
        private void RestoreAll()
        {
            if (Field.IsIdentity) return;
            _undo.Push(Field.Clone());
            Field.Reset();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(HasChanges));
            RenderPreview();
        }

        /// <summary>[ and ] - like Photoshop.</summary>
        public void ResizeBrush(bool bigger) =>
            BrushSize = Math.Clamp(bigger ? BrushSize * 1.15 : BrushSize / 1.15, 5, 1500);

        // ----- Output -----

        /// <summary>The full-size result for <paramref name="source"/> (the layer's
        /// untouched pixels).</summary>
        public SKBitmap ApplyTo(SKBitmap source)
        {
            if (source.Width != _imageWidth || source.Height != _imageHeight)
                throw new ArgumentException("The source must be the image the dialog was opened with.", nameof(source));
            return Field.IsIdentity ? source.Copy() : Field.Apply(source);
        }

        private void RenderPreview()
        {
            if (Preview is null) return; // the constructor isn't done yet
            using var warped = Field.IsIdentity ? _previewSource.Copy() : Field.Apply(_previewSource);
            using var bgra = new SKBitmap(PreviewWidth, PreviewHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bgra))
            {
                canvas.Clear(SKColors.Transparent);
                if (ShowBackdrop && _previewBackdrop is not null) canvas.DrawBitmap(_previewBackdrop, 0, 0);
                canvas.DrawBitmap(warped, 0, 0);
            }
            Preview.WritePixels(new Int32Rect(0, 0, PreviewWidth, PreviewHeight), bgra.GetPixels(), bgra.ByteCount, bgra.RowBytes);
        }

        public void Dispose()
        {
            _previewSource.Dispose();
            _previewBackdrop?.Dispose();
        }
    }
}
