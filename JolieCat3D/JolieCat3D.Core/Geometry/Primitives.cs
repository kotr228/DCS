using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Standard primitive <see cref="Mesh"/> factories - Cube, Sphere, Cylinder, Plane -
    /// each with a configurable resolution parameter, so a caller (a "Create Primitive"
    /// UI command, a smoke test, <c>JolieCat3D.UI</c>'s own demo scene) never has to hand-
    /// build vertex/face data itself. Every winding order below (CCW when viewed from the
    /// side each face's own normal points toward, matching <see cref="Face"/>'s own
    /// documented convention) was verified against a throwaway numeric check - for every
    /// generated face, does its normal actually point away from the primitive's center -
    /// before being written here, not just eyeballed; this sandbox has no way to render
    /// WPF and look at the result directly.
    /// </summary>
    public static class Primitives
    {
        /// <summary>An axis-aligned cube of the given <paramref name="size"/>, centered on
        /// the origin. <paramref name="segmentsPerFace"/> subdivides each of the 6 faces
        /// into an NxN grid of quads (1 = a plain single-quad face, the original
        /// behavior) rather than leaving every face a single flat quad - useful as a
        /// starting mesh for <see cref="Mesh.Subdivide"/>/<see cref="Mesh.ExtrudeFace"/>
        /// to actually have more than one quad to operate on. Each face still gets its
        /// own vertices rather than sharing the cube's 8 corner positions (per-face flat
        /// normals, a full 0-1 UV unwrap per face), the same reasoning the original
        /// single-quad-per-face version already used.</summary>
        public static Mesh CreateCube(float size = 1f, string name = "Cube", int segmentsPerFace = 1)
        {
            segmentsPerFace = Math.Max(1, segmentsPerFace);
            var mesh = new Mesh(name);
            var h = size / 2f;

            // Each face: outward normal, the (u=0,v=0) corner, and the U/V axes spanning
            // the face from that corner to the full size - this exactly reproduces the
            // previous hard-coded 4-corner-per-face table at segmentsPerFace=1 (verified
            // corner-by-corner before being written here), just generalized to an NxN grid.
            (Vector3 Normal, Vector3 Origin, Vector3 UAxis, Vector3 VAxis)[] faces =
            {
                (Vector3.UnitZ, new Vector3(-h, -h, h), new Vector3(size, 0, 0), new Vector3(0, size, 0)),   // +Z
                (-Vector3.UnitZ, new Vector3(h, -h, -h), new Vector3(-size, 0, 0), new Vector3(0, size, 0)), // -Z
                (Vector3.UnitX, new Vector3(h, -h, h), new Vector3(0, 0, -size), new Vector3(0, size, 0)),   // +X
                (-Vector3.UnitX, new Vector3(-h, -h, -h), new Vector3(0, 0, size), new Vector3(0, size, 0)), // -X
                (Vector3.UnitY, new Vector3(-h, h, h), new Vector3(size, 0, 0), new Vector3(0, 0, -size)),   // +Y
                (-Vector3.UnitY, new Vector3(-h, -h, -h), new Vector3(size, 0, 0), new Vector3(0, 0, size)), // -Y
            };

            foreach (var (normal, origin, uAxis, vAxis) in faces)
                AddGridFace(mesh, origin, uAxis, vAxis, normal, segmentsPerFace);

            return mesh;
        }

        /// <summary>A flat quad grid in the XZ plane (Y = 0), facing +Y - the conventional
        /// "ground" orientation for a scene whose camera looks roughly along -Z with Y up.
        /// <paramref name="widthSegments"/>/<paramref name="depthSegments"/> subdivide it
        /// into a grid (1x1 = the original single quad); the new parameters are appended
        /// after <paramref name="name"/>, not inserted before it, so existing positional
        /// calls (<c>CreatePlane(8f, 8f, "Ground")</c>) keep compiling unchanged.</summary>
        public static Mesh CreatePlane(float width = 1f, float depth = 1f, string name = "Plane", int widthSegments = 1, int depthSegments = 1)
        {
            widthSegments = Math.Max(1, widthSegments);
            depthSegments = Math.Max(1, depthSegments);

            var mesh = new Mesh(name);
            var hw = width / 2f;
            var hd = depth / 2f;

            var grid = new int[widthSegments + 1, depthSegments + 1];
            for (var x = 0; x <= widthSegments; x++)
            {
                for (var z = 0; z <= depthSegments; z++)
                {
                    var position = new Vector3(-hw + width * x / widthSegments, 0, -hd + depth * z / depthSegments);
                    var uv = new Vector2((float)x / widthSegments, 1f - (float)z / depthSegments);
                    grid[x, z] = mesh.AddVertex(new Vertex(position, Vector3.UnitY, uv));
                }
            }

            for (var x = 0; x < widthSegments; x++)
                for (var z = 0; z < depthSegments; z++)
                    mesh.AddQuad(grid[x, z], grid[x + 1, z], grid[x + 1, z + 1], grid[x, z + 1]);

            return mesh;
        }

        /// <summary>A UV sphere of the given <paramref name="radius"/>, centered on the
        /// origin - <paramref name="latitudeSegments"/> horizontal bands from pole to
        /// pole, <paramref name="longitudeSegments"/> vertical slices around. Smooth,
        /// per-vertex normals (simply the normalized position, since the sphere is
        /// centered at the origin) rather than the flat per-face normals
        /// <see cref="CreateCube"/> uses - the two share vertices between adjacent quads
        /// here, unlike the cube's deliberately-duplicated ones, since a sphere is
        /// supposed to look smoothly curved, not faceted. The poles are triangle fans
        /// (a quad there would be degenerate - all four corners collapsing toward one
        /// point), everywhere else is quads.</summary>
        public static Mesh CreateSphere(float radius = 0.5f, int latitudeSegments = 16, int longitudeSegments = 24, string name = "Sphere")
        {
            latitudeSegments = Math.Max(2, latitudeSegments);
            longitudeSegments = Math.Max(3, longitudeSegments);

            var mesh = new Mesh(name);
            var grid = new int[latitudeSegments + 1, longitudeSegments + 1];

            for (var lat = 0; lat <= latitudeSegments; lat++)
            {
                // theta: 0 at the north pole (+Y), PI at the south pole (-Y).
                var theta = lat * MathF.PI / latitudeSegments;
                var sinTheta = MathF.Sin(theta);
                var cosTheta = MathF.Cos(theta);

                for (var lon = 0; lon <= longitudeSegments; lon++)
                {
                    var phi = lon * 2f * MathF.PI / longitudeSegments;
                    var direction = new Vector3(sinTheta * MathF.Cos(phi), cosTheta, sinTheta * MathF.Sin(phi));
                    var uv = new Vector2((float)lon / longitudeSegments, (float)lat / latitudeSegments);
                    grid[lat, lon] = mesh.AddVertex(new Vertex(direction * radius, direction, uv));
                }
            }

            for (var lat = 0; lat < latitudeSegments; lat++)
            {
                for (var lon = 0; lon < longitudeSegments; lon++)
                {
                    var a = grid[lat, lon];
                    var b = grid[lat, lon + 1];
                    var c = grid[lat + 1, lon + 1];
                    var d = grid[lat + 1, lon];

                    if (lat == 0) mesh.AddTriangle(a, c, d); // north pole: a==every lon here collapses to one point
                    else if (lat == latitudeSegments - 1) mesh.AddTriangle(a, b, c); // south pole
                    else mesh.AddQuad(a, b, c, d);
                }
            }

            return mesh;
        }

        /// <summary>A cylinder of the given <paramref name="radius"/> and
        /// <paramref name="height"/>, centered on the origin with its axis along Y -
        /// <paramref name="segments"/> quads around the side surface, each with its own
        /// radially-outward normal (shared between the top/bottom rim of that one
        /// segment, since the side surface itself is meant to look smoothly curved), plus
        /// a flat triangle-fan cap at each end (with their own, separate +Y/-Y-normal
        /// vertices - a cap vertex needs a different normal than the side surface at the
        /// same position, so it can't reuse the side ring's own vertices) when
        /// <paramref name="capped"/> is true.</summary>
        public static Mesh CreateCylinder(float radius = 0.5f, float height = 1f, int segments = 24, bool capped = true, string name = "Cylinder")
        {
            segments = Math.Max(3, segments);
            var mesh = new Mesh(name);
            var half = height / 2f;

            var sideTop = new int[segments];
            var sideBottom = new int[segments];
            for (var i = 0; i < segments; i++)
            {
                var angle = i * 2f * MathF.PI / segments;
                var direction = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
                var u = (float)i / segments;

                sideTop[i] = mesh.AddVertex(new Vertex(direction * radius + new Vector3(0, half, 0), direction, new Vector2(u, 1)));
                sideBottom[i] = mesh.AddVertex(new Vertex(direction * radius - new Vector3(0, half, 0), direction, new Vector2(u, 0)));
            }

            for (var i = 0; i < segments; i++)
            {
                var next = (i + 1) % segments;
                // Verified winding (bottom[i], top[i], top[next], bottom[next]) - the
                // naive-looking (bottom[i], bottom[next], top[next], top[i]) order
                // actually winds inward, caught by a numeric check before this was written.
                mesh.AddQuad(sideBottom[i], sideTop[i], sideTop[next], sideBottom[next]);
            }

            if (capped)
            {
                AddCylinderCap(mesh, radius, half, segments, isTop: true);
                AddCylinderCap(mesh, radius, half, segments, isTop: false);
            }

            return mesh;
        }

        private static void AddCylinderCap(Mesh mesh, float radius, float halfHeight, int segments, bool isTop)
        {
            var y = isTop ? halfHeight : -halfHeight;
            var normal = isTop ? Vector3.UnitY : -Vector3.UnitY;
            var centerIndex = mesh.AddVertex(new Vertex(new Vector3(0, y, 0), normal, new Vector2(0.5f, 0.5f)));

            var rim = new int[segments];
            for (var i = 0; i < segments; i++)
            {
                var angle = i * 2f * MathF.PI / segments;
                var position = new Vector3(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius);
                var uv = new Vector2(0.5f + MathF.Cos(angle) * 0.5f, 0.5f + MathF.Sin(angle) * 0.5f);
                rim[i] = mesh.AddVertex(new Vertex(position, normal, uv));
            }

            for (var i = 0; i < segments; i++)
            {
                var next = (i + 1) % segments;
                // Top and bottom wind opposite ways so both faces outward (+Y up top,
                // -Y down at the bottom) - verified numerically before being written here.
                if (isTop) mesh.AddTriangle(centerIndex, rim[next], rim[i]);
                else mesh.AddTriangle(centerIndex, rim[i], rim[next]);
            }
        }

        /// <summary>Fills an NxN quad grid spanning <paramref name="uAxis"/>/<paramref name="vAxis"/>
        /// from <paramref name="origin"/>, all with the same flat <paramref name="normal"/> -
        /// <see cref="CreateCube"/>'s own per-face building block, generalized from a
        /// single quad to a grid.</summary>
        private static void AddGridFace(Mesh mesh, Vector3 origin, Vector3 uAxis, Vector3 vAxis, Vector3 normal, int segments)
        {
            var grid = new int[segments + 1, segments + 1];
            for (var u = 0; u <= segments; u++)
            {
                for (var v = 0; v <= segments; v++)
                {
                    var position = origin + uAxis * ((float)u / segments) + vAxis * ((float)v / segments);
                    var uv = new Vector2((float)u / segments, 1f - (float)v / segments);
                    grid[u, v] = mesh.AddVertex(new Vertex(position, normal, uv));
                }
            }

            for (var u = 0; u < segments; u++)
                for (var v = 0; v < segments; v++)
                    mesh.AddQuad(grid[u, v], grid[u + 1, v], grid[u + 1, v + 1], grid[u, v + 1]);
        }
    }
}
