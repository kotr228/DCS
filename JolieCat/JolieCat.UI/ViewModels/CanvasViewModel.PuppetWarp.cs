using System.Windows.Input;
using JolieCat.Core.Transform;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Puppet Warp tool: click to stick pins into the active layer, drag a pin to bend
    /// the image around the others, Alt+click a pin to remove it. Enter (or switching
    /// tools) applies it as one undo step, Esc cancels.
    /// </summary>
    public partial class CanvasViewModel
    {
        private PuppetWarp? _puppet;
        private Core.Documents.Layer? _puppetLayer;
        private SKBitmap? _puppetOriginal;
        private int _puppetDragPin = -1;

        /// <summary>The live puppet (pins and mesh), or null while the tool isn't in use.</summary>
        public PuppetWarp? Puppet => _puppet;

        /// <summary>The pin being dragged (drawn highlighted), or -1.</summary>
        public int ActivePuppetPin => _puppetDragPin;

        public bool ShowPuppetMesh => (_toolbox.CurrentToolOptions as PuppetWarpToolOptionsViewModel)?.ShowMesh ?? true;

        private void StartPuppetWarp()
        {
            var layer = Layers.ActiveLayer?.Model;
            if (layer is null || layer.IsGroup || layer.IsAdjustment || layer.IsLocked) return;
            if (layer.HasSmartFilters)
            {
                ToolHintText = "Puppet Warp works on pixels - rasterize the Smart Filters first";
                return;
            }
            RasterizeIfText(layer);

            _puppetLayer = layer;
            _puppetOriginal?.Dispose();
            _puppetOriginal = layer.Bitmap.Copy();
            var options = _toolbox.CurrentToolOptions as PuppetWarpToolOptionsViewModel;
            _puppet = new PuppetWarp(layer.Bitmap.Width, layer.Bitmap.Height, (float)(options?.Density ?? 16))
            {
                Rigidity = (float)(options?.Rigidity ?? 1),
            };
            ToolHintText = "Click to add pins, drag a pin to bend the image, Alt+click a pin to remove it; Enter applies, Esc cancels";
            RaiseInvalidate();
        }

        private bool PressPuppetWarp(SKPoint doc)
        {
            if (_puppet is null)
            {
                StartPuppetWarp();
                if (_puppet is null) return true;
            }

            var reach = 8 / (float)System.Math.Max(Zoom, 0.01);
            var hit = _puppet.PinAt(doc, reach);
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                if (hit >= 0)
                {
                    _puppet.Pins.RemoveAt(hit);
                    _puppet.Update();
                }
            }
            else if (hit >= 0)
            {
                _puppetDragPin = hit;
            }
            else
            {
                _puppet.Pins.Add(new PuppetWarp.Pin(_puppet.Unmap(doc)));
                _puppet.Pins[^1].Current = doc;
                _puppetDragPin = _puppet.Pins.Count - 1;
            }
            RaiseInvalidate();
            return true;
        }

        private bool MovePuppetPin(SKPoint doc)
        {
            if (_puppet is null || _puppetDragPin < 0 || _puppetDragPin >= _puppet.Pins.Count) return false;
            _puppet.Pins[_puppetDragPin].Current = doc;
            if (_toolbox.CurrentToolOptions is PuppetWarpToolOptionsViewModel options) _puppet.Rigidity = (float)options.Rigidity;
            _puppet.Update();
            RaiseInvalidate();
            return true;
        }

        private void ReleasePuppetPin() => _puppetDragPin = -1;

        /// <summary>Applies the warp to the layer (one undo step); a no-op if nothing moved.</summary>
        public void CommitPuppetWarp()
        {
            if (_puppet is null || _puppetLayer is null || _puppetOriginal is null)
            {
                EndPuppetWarp();
                return;
            }

            var moved = _puppet.Pins.Exists(p => p.Current != p.Original);
            if (moved)
            {
                var before = _puppetLayer.Bitmap.Pixels;
                using (var baked = _puppet.Mesh.Bake(_puppetOriginal))
                    _puppetLayer.Bitmap.Pixels = baked.Pixels;
                Layers.History.Push(PixelEdit(_puppetLayer, before, contentOnly: true), "Puppet Warp");
            }
            EndPuppetWarp();
            RaiseInvalidate();
        }

        public void CancelPuppetWarp()
        {
            EndPuppetWarp();
            RaiseInvalidate();
        }

        private void EndPuppetWarp()
        {
            _puppet = null;
            _puppetLayer = null;
            _puppetOriginal?.Dispose();
            _puppetOriginal = null;
            _puppetDragPin = -1;
        }

        /// <summary>The live warped preview of the layer, while pins have moved.</summary>
        private SKBitmap? BuildPuppetPreview() =>
            _puppet is not null && _puppetOriginal is not null && _puppet.Pins.Exists(p => p.Current != p.Original)
                ? _puppet.Mesh.Bake(_puppetOriginal)
                : null;
    }
}
