using System.Numerics;
using System.Windows;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Engine.Rendering;

namespace JolieCat3D.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            _renderer = new Scene3DRenderer(Viewport);
            _renderer.Render(BuildDemoScene());
        }

        private readonly Scene3DRenderer _renderer;

        /// <summary>
        /// A small, self-contained scene proving every layer of the pipeline the task
        /// this window exists to verify actually connects end to end: a
        /// <see cref="JolieCat3D.Core"/> <see cref="Mesh"/> (from <see cref="Primitives.CreateCube"/>),
        /// a <see cref="Material"/>, a parent/child <see cref="Node"/> hierarchy (proving
        /// <see cref="Node.GetWorldTransform"/>'s composition, not just one node's own
        /// local transform), assembled into a <see cref="Scene3D"/> and handed to
        /// <see cref="Scene3DRenderer.Render"/> - the exact same call any future
        /// real-content loader in this project would make.
        /// </summary>
        private static Scene3D BuildDemoScene()
        {
            var scene = new Scene3D("Demo Scene");

            var goldMaterial = new Material("Gold")
            {
                DiffuseColor = new Color4(0.85f, 0.65f, 0.13f),
                SpecularColor = new Color4(1f, 1f, 1f),
                SpecularPower = 40.0,
            };

            var parentCube = Primitives.CreateCube(1.5f, "ParentCube");
            parentCube.Material = goldMaterial;

            var parentNode = new Node("Parent")
            {
                Mesh = parentCube,
                // A small constant spin, just so the parent cube isn't perfectly
                // axis-aligned - makes it obvious at a glance that LocalRotation is
                // actually being applied, not silently ignored.
                LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 6f),
            };
            scene.AddRootNode(parentNode);

            var emeraldMaterial = new Material("Emerald")
            {
                DiffuseColor = new Color4(0.1f, 0.6f, 0.35f),
                SpecularColor = new Color4(0.8f, 0.8f, 0.8f),
                SpecularPower = 60.0,
            };

            var childCube = Primitives.CreateCube(0.5f, "ChildCube");
            childCube.Material = emeraldMaterial;

            // Offset and scaled relative to the parent - orbiting the demo scene should
            // show this cube riding along with the parent's own position/rotation, not
            // sitting fixed in world space, proving Node.GetWorldTransform's parent/child
            // composition (see SceneGraphBuilder) rather than just one flat node list.
            var childNode = new Node("Child")
            {
                Mesh = childCube,
                LocalPosition = new Vector3(2.0f, 0.75f, 0f),
                LocalScale = new Vector3(0.75f, 0.75f, 0.75f),
            };
            parentNode.AddChild(childNode);

            var groundMaterial = new Material("Ground")
            {
                DiffuseColor = new Color4(0.5f, 0.5f, 0.5f),
                SpecularColor = Color4.Black,
            };

            var groundMesh = Primitives.CreatePlane(8f, 8f, "Ground");
            groundMesh.Material = groundMaterial;

            var groundNode = new Node("GroundPlane")
            {
                Mesh = groundMesh,
                LocalPosition = new Vector3(0f, -1.5f, 0f),
            };
            scene.AddRootNode(groundNode);

            return scene;
        }
    }
}
