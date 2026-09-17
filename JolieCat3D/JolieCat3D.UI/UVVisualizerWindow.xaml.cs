using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using JolieCat3D.Core.Geometry;
using IoPath = System.IO.Path;

namespace JolieCat3D.UI
{
    /// <summary>
    /// A dedicated, non-modal UV Map Visualizer - draws <see cref="Mesh.GetRenderFaces"/>'s
    /// own UV-space wireframe (see <see cref="UVWireframe.GetEdges"/>) over the
    /// selected node's currently-loaded diffuse texture (if any), so a user can see
    /// exactly how the 3D geometry's UVs align with whatever they're painting in the 2D
    /// JolieCat app. Built ONCE from whatever <see cref="Mesh"/>/texture path
    /// <c>MainWindow</c> hands the constructor at the moment it's opened - a later UV
    /// Projection or texture change on that same node does not live-update an
    /// already-open window; closing and reopening it is how a user sees the new state,
    /// the same "snapshot, not a live view" simplification this project's own <c>NodeViewModel</c>
    /// constructor pattern (<c>SyncFromCore</c> called explicitly, not on every tick)
    /// already established elsewhere.
    /// </summary>
    public partial class UVVisualizerWindow : Window
    {
        private const double CanvasSize = 512;

        private static readonly Brush WireBrush = CreateWireBrush();

        public UVVisualizerWindow(Mesh mesh, string? diffuseTexturePath, string nodeName)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            InitializeComponent();

            Title = $"UV Map - {nodeName}";

            var faceCount = mesh.GetRenderFaces().Count();
            SummaryText.Text = $"{nodeName} - {faceCount} triangle{(faceCount == 1 ? "" : "s")}"
                + (string.IsNullOrWhiteSpace(diffuseTexturePath) ? " (no texture loaded)" : "");

            TextureImage.Source = TryLoadTexture(diffuseTexturePath);

            foreach (var (a, b) in UVWireframe.GetEdges(mesh))
            {
                // UV (0,0) is this project's own established bottom-left origin (see
                // Core.Materials.Material.DiffuseTextureOffset's own remarks) - Canvas
                // (0,0) is top-left, so V is flipped here, once, rather than baking the
                // flip into UVWireframe itself (which has no business knowing anything
                // about screen-space coordinate conventions at all).
                var line = new Line
                {
                    X1 = a.X * CanvasSize,
                    Y1 = (1 - a.Y) * CanvasSize,
                    X2 = b.X * CanvasSize,
                    Y2 = (1 - b.Y) * CanvasSize,
                    Stroke = WireBrush,
                    StrokeThickness = 1,
                };
                UVCanvas.Children.Add(line);
            }
        }

        /// <summary>Loads <paramref name="path"/> as a frozen <see cref="BitmapImage"/> -
        /// null (leaving the background plain black, per <see cref="UVVisualizerWindow.xaml"/>'s
        /// own <c>Border.Background</c>) for a null/blank path, a missing file, or any
        /// decode failure - the same "a broken texture reference degrades visibly rather
        /// than crashing" tolerance <c>Engine.Geometry.MaterialFactory.CreateDiffuseBrush</c>
        /// already established for exactly this kind of path.</summary>
        private static BitmapImage? TryLoadTexture(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(IoPath.GetFullPath(path), UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Brush CreateWireBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xC2, 0x9B, 0x58)); // AccentBrush's own gold
            brush.Freeze();
            return brush;
        }
    }
}
