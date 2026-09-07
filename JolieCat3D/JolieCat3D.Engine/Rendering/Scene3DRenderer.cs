using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Engine.Camera;
using JolieCat3D.Engine.Geometry;
using JolieCat3D.Engine.Lighting;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// The top-level facade tying every other <c>JolieCat3D.Engine</c> piece together:
    /// bind one instance to a <see cref="HelixViewport3D"/> (typically once, e.g. from a
    /// window's constructor), configure standard lighting and orbit/pan/zoom camera
    /// controls once via <see cref="Attach"/>, then call <see cref="Render"/> every time
    /// the <c>JolieCat3D.Core</c> scene it's showing changes. This is the one class a
    /// consumer (<c>JolieCat3D.UI</c>) actually needs to know about to get a
    /// <see cref="CoreScene"/> on screen - it owns no rendering logic of its own beyond
    /// delegating to <see cref="SceneGraphBuilder"/>, <see cref="SceneLightingFactory"/>,
    /// and <see cref="CameraFraming"/>.
    /// </summary>
    public sealed class Scene3DRenderer
    {
        private readonly HelixViewport3D _viewport;
        private readonly ModelVisual3D _lightingVisual = new();
        private readonly ModelVisual3D _sceneVisual = new();
        private bool _isAttached;

        public LightingSettings Lighting { get; set; } = LightingSettings.CreateDefault();

        public Scene3DRenderer(HelixViewport3D viewport) =>
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

        /// <summary>
        /// One-time setup: configures orbit/pan/zoom (see <see cref="CameraFraming.ConfigureOrbitPanZoom"/>)
        /// and adds this renderer's own lighting/scene visuals to <see cref="_viewport"/>'s
        /// children. Idempotent - <see cref="Render"/> calls this itself, so a caller
        /// never strictly needs to call it directly, but doing so explicitly (e.g. right
        /// after constructing the viewport, before any scene exists yet) shows an empty,
        /// correctly-lit viewport immediately rather than a completely blank one.
        /// </summary>
        public void Attach()
        {
            if (_isAttached) return;

            CameraFraming.ConfigureOrbitPanZoom(_viewport);

            _lightingVisual.Content = SceneLightingFactory.CreateStandardLighting(Lighting);
            _viewport.Children.Add(_lightingVisual);
            _viewport.Children.Add(_sceneVisual);

            _isAttached = true;
        }

        /// <summary>Rebuilds the lighting visual from <see cref="Lighting"/>'s current
        /// settings - call after changing them; <see cref="Attach"/>/<see cref="Render"/>
        /// already do this once on their own.</summary>
        public void RefreshLighting()
        {
            Attach();
            _lightingVisual.Content = SceneLightingFactory.CreateStandardLighting(Lighting);
        }

        /// <summary>
        /// Maps <paramref name="scene"/>'s whole node tree into GPU-renderable
        /// <see cref="Model3DGroup"/>/<see cref="MeshGeometry3D"/> content (see
        /// <see cref="SceneGraphBuilder"/>) and shows it - replacing whatever this
        /// renderer showed last, so calling this again after editing
        /// <paramref name="scene"/> is how a caller re-renders it. <paramref name="zoomToFit"/>
        /// additionally reframes the camera on the scene's own bounds each time (see
        /// <see cref="CameraFraming.ZoomToFit"/>) - on by default since a freshly
        /// replaced scene is otherwise not guaranteed to still be inside the previous
        /// scene's own framing.
        /// </summary>
        public void Render(CoreScene scene, bool zoomToFit = true)
        {
            ArgumentNullException.ThrowIfNull(scene);
            Attach();

            _sceneVisual.Content = SceneGraphBuilder.Build(scene);

            if (zoomToFit) CameraFraming.ZoomToFit(_viewport, scene);
        }
    }
}
