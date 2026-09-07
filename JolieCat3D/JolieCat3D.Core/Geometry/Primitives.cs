using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// A couple of hand-built primitive <see cref="Mesh"/> factories - not asked for by
    /// name, but a minimal, obviously-useful piece of scaffolding every 3D engine's Core
    /// ships: something to actually build a <see cref="Scene.Node"/>/<see cref="Mesh"/>
    /// out of, for a demo scene, a smoke test, or a placeholder while real content-loading
    /// doesn't exist yet (see <c>JolieCat3D.UI</c>'s own use of <see cref="CreateCube"/>).
    /// </summary>
    public static class Primitives
    {
        /// <summary>An axis-aligned cube of the given <paramref name="size"/>, centered on
        /// the origin, with per-face flat normals (each of the 6 faces gets its own 4
        /// vertices, rather than sharing the cube's 8 corner positions, so a corner isn't
        /// forced to average three different face normals into one smoothed-looking one)
        /// and a full 0-1 UV unwrap per face.</summary>
        public static Mesh CreateCube(float size = 1f, string name = "Cube")
        {
            var mesh = new Mesh(name);
            var h = size / 2f;

            // Each entry is one face: its outward normal, plus its 4 corners in
            // counter-clockwise winding when viewed from outside (from the normal's side).
            (Vector3 Normal, Vector3[] Corners)[] faces =
            {
                (Vector3.UnitZ, new[] { new Vector3(-h, -h, h), new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h) }), // +Z
                (-Vector3.UnitZ, new[] { new Vector3(h, -h, -h), new Vector3(-h, -h, -h), new Vector3(-h, h, -h), new Vector3(h, h, -h) }), // -Z
                (Vector3.UnitX, new[] { new Vector3(h, -h, h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(h, h, h) }), // +X
                (-Vector3.UnitX, new[] { new Vector3(-h, -h, -h), new Vector3(-h, -h, h), new Vector3(-h, h, h), new Vector3(-h, h, -h) }), // -X
                (Vector3.UnitY, new[] { new Vector3(-h, h, h), new Vector3(h, h, h), new Vector3(h, h, -h), new Vector3(-h, h, -h) }), // +Y
                (-Vector3.UnitY, new[] { new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h), new Vector3(-h, -h, h) }), // -Y
            };

            Vector2[] faceUVs = { new(0, 1), new(1, 1), new(1, 0), new(0, 0) };

            foreach (var (normal, corners) in faces)
            {
                var baseIndex = mesh.Vertices.Count;
                for (var i = 0; i < 4; i++)
                    mesh.AddVertex(new Vertex(corners[i], normal, faceUVs[i]));

                mesh.AddQuad(baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 3);
            }

            return mesh;
        }

        /// <summary>A flat, single-quad plane in the XZ plane (Y = 0), facing +Y - the
        /// conventional "ground" orientation for a scene whose camera looks roughly
        /// along -Z with Y up.</summary>
        public static Mesh CreatePlane(float width = 1f, float depth = 1f, string name = "Plane")
        {
            var mesh = new Mesh(name);
            var hw = width / 2f;
            var hd = depth / 2f;

            mesh.AddVertex(new Vertex(new Vector3(-hw, 0, -hd), Vector3.UnitY, new Vector2(0, 1)));
            mesh.AddVertex(new Vertex(new Vector3(hw, 0, -hd), Vector3.UnitY, new Vector2(1, 1)));
            mesh.AddVertex(new Vertex(new Vector3(hw, 0, hd), Vector3.UnitY, new Vector2(1, 0)));
            mesh.AddVertex(new Vertex(new Vector3(-hw, 0, hd), Vector3.UnitY, new Vector2(0, 0)));
            mesh.AddQuad(0, 1, 2, 3);

            return mesh;
        }
    }
}
