using System.Windows.Controls;
using JolieCat.UI.Rendering;
using SkiaSharp.Views.Desktop;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// Hosts the hardware-accelerated Skia render surface for the document canvas.
    /// Rendering itself is delegated to <see cref="CanvasRenderer"/> so this code-behind
    /// stays a thin adapter between the WPF control and the render logic.
    /// </summary>
    public partial class CanvasView : UserControl
    {
        public CanvasView()
        {
            InitializeComponent();
        }

        private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
        {
            CanvasRenderer.Render(e.Surface.Canvas, e.Info);
        }
    }
}
