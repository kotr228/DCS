using System;
using SkiaSharp;

namespace JolieCat.Core.Animation
{
    /// <summary>
    /// Bends a picture at a joint, like an arm at the elbow: the picture is laid on a
    /// mesh of triangles, and every point past <c>joint</c> (seen from <c>root</c>) turns
    /// around it by the bend angle. Across a soft band either side of the joint the turn
    /// fades in, so the bend is a smooth curve instead of a crease.
    /// </summary>
    public static class MeshBend
    {
        /// <summary>Where <paramref name="point"/> goes when bent.</summary>
        public static SKPoint Map(SKPoint point, SKPoint root, SKPoint joint, double degrees, float softness)
        {
            var axis = joint - root;
            var length = axis.Length;
            var direction = length < 1e-3f ? new SKPoint(1, 0) : new SKPoint(axis.X / length, axis.Y / length);
            var along = (point.X - joint.X) * direction.X + (point.Y - joint.Y) * direction.Y;
            var s = Math.Clamp((along + softness) / (2 * softness), 0f, 1f);
            var weight = s * s * (3 - 2 * s);
            if (weight <= 0) return point;
            var angle = degrees * weight * Math.PI / 180;
            var (sin, cos) = Math.SinCos(angle);
            var dx = point.X - joint.X;
            var dy = point.Y - joint.Y;
            return new SKPoint((float)(joint.X + dx * cos - dy * sin), (float)(joint.Y + dx * sin + dy * cos));
        }

        /// <summary>The soft band's half width: a quarter of the root-to-joint length, at least 6 px.</summary>
        public static float SoftnessFor(SKPoint root, SKPoint joint) => Math.Max(6f, (joint - root).Length * 0.25f);

        /// <summary>A bent copy of <paramref name="source"/> (same size).</summary>
        public static SKBitmap Bend(SKBitmap source, SKPoint root, SKPoint joint, double degrees, int cells = 32)
        {
            var w = source.Width;
            var h = source.Height;
            var softness = SoftnessFor(root, joint);
            var n = Math.Clamp(cells, 2, 120);
            var columns = n + 1;
            var positions = new SKPoint[columns * columns];
            var texture = new SKPoint[positions.Length];
            for (var j = 0; j <= n; j++)
                for (var i = 0; i <= n; i++)
                {
                    var p = new SKPoint(w * i / (float)n, h * j / (float)n);
                    texture[j * columns + i] = p;
                    positions[j * columns + i] = Map(p, root, joint, degrees, softness);
                }

            var indices = new ushort[n * n * 6];
            var k = 0;
            for (var j = 0; j < n; j++)
                for (var i = 0; i < n; i++)
                {
                    var a = (ushort)(j * columns + i);
                    var b = (ushort)(a + 1);
                    var c = (ushort)(a + columns);
                    var d = (ushort)(c + 1);
                    indices[k++] = a; indices[k++] = b; indices[k++] = c;
                    indices[k++] = b; indices[k++] = d; indices[k++] = c;
                }

            var result = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            using var image = SKImage.FromBitmap(source);
            using var shader = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, new SKSamplingOptions(SKFilterMode.Linear));
            using var paint = new SKPaint { Shader = shader, IsAntialias = true };
            using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, texture, null, indices);
            canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
            return result;
        }
    }
}
