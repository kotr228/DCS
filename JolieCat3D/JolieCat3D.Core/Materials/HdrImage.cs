using System.Globalization;
using System.Numerics;
using System.Text;

namespace JolieCat3D.Core.Materials
{
    /// <summary>
    /// A linear, unclamped float RGB image (lightmaps, environment maps) with Radiance
    /// .hdr (RGBE) read/write - the HDR format practically every game engine imports.
    /// Row 0 is the top of the image, like <see cref="TextureBuffer"/>.
    /// </summary>
    public sealed class HdrImage
    {
        public int Width { get; }
        public int Height { get; }
        public Vector3[] Pixels { get; }

        public HdrImage(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            Pixels = new Vector3[width * height];
        }

        public Vector3 this[int x, int y]
        {
            get => Pixels[y * Width + x];
            set => Pixels[y * Width + x] = value;
        }

        /// <summary>
        /// Bilinear lookup treating the image as an equirectangular (lat-long) panorama:
        /// the image centre looks down -Z (forward), the top row is straight up (+Y), and
        /// u grows towards +X. <paramref name="rotationDegrees"/> turns the panorama about +Y.
        /// </summary>
        public Vector3 SampleEquirect(Vector3 direction, float rotationDegrees = 0f)
        {
            if (rotationDegrees != 0f)
                direction = Vector3.Transform(direction, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -rotationDegrees * MathF.PI / 180f));
            var length = direction.Length();
            if (!(length > 1e-12f)) return Vector3.Zero;
            direction /= length;
            var u = 0.5f + MathF.Atan2(direction.X, -direction.Z) / (2f * MathF.PI);
            var v = MathF.Acos(Math.Clamp(direction.Y, -1f, 1f)) / MathF.PI;

            var fx = u * Width - 0.5f;
            var fy = Math.Clamp(v * Height - 0.5f, 0f, Height - 1);
            var x0 = (int)MathF.Floor(fx);
            var y0 = (int)MathF.Floor(fy);
            var tx = fx - x0;
            var ty = fy - y0;
            int Wrap(int x) => ((x % Width) + Width) % Width;
            var y1 = Math.Min(y0 + 1, Height - 1);
            var top = Vector3.Lerp(this[Wrap(x0), y0], this[Wrap(x0 + 1), y0], tx);
            var bottom = Vector3.Lerp(this[Wrap(x0), y1], this[Wrap(x0 + 1), y1], tx);
            return Vector3.Lerp(top, bottom, ty);
        }

        /// <summary>The direction through texel coordinates (<paramref name="u"/>,
        /// <paramref name="v"/> in 0..1) of D3D cube face <paramref name="face"/>
        /// (+X, -X, +Y, -Y, +Z, -Z) - the inverse of hardware cube sampling.</summary>
        public static Vector3 CubeFaceDirection(int face, float u, float v)
        {
            var sc = 2f * u - 1f;
            var tc = 2f * v - 1f;
            var d = face switch
            {
                0 => new Vector3(1, -tc, -sc),
                1 => new Vector3(-1, -tc, sc),
                2 => new Vector3(sc, 1, tc),
                3 => new Vector3(sc, -1, -tc),
                4 => new Vector3(sc, -tc, 1),
                _ => new Vector3(-sc, -tc, -1),
            };
            return Vector3.Normalize(d);
        }

        /// <summary>Resamples the panorama into six <paramref name="size"/>² cube faces
        /// (D3D order), still linear.</summary>
        public Vector3[][] ToCubeFaces(int size, float rotationDegrees = 0f)
        {
            var faces = new Vector3[6][];
            for (var f = 0; f < 6; f++)
            {
                var face = new Vector3[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    face[y * size + x] = SampleEquirect(CubeFaceDirection(f, (x + 0.5f) / size, (y + 0.5f) / size), rotationDegrees);
                faces[f] = face;
            }
            return faces;
        }

        /// <summary>Writes an uncompressed Radiance RGBE file (-Y height +X width).</summary>
        public void SaveHdr(string path)
        {
            using var stream = File.Create(path);
            WriteHdr(stream);
        }

        public void WriteHdr(Stream stream)
        {
            var header = Encoding.ASCII.GetBytes(
                $"#?RADIANCE\n# JolieCat3D\nFORMAT=32-bit_rle_rgbe\n\n-Y {Height.ToString(CultureInfo.InvariantCulture)} +X {Width.ToString(CultureInfo.InvariantCulture)}\n");
            stream.Write(header);
            var row = new byte[Width * 4];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var (r, g, b, e) = ToRgbe(this[x, y]);
                    row[x * 4] = r; row[x * 4 + 1] = g; row[x * 4 + 2] = b; row[x * 4 + 3] = e;
                }
                stream.Write(row);
            }
        }

        public static HdrImage LoadHdr(string path)
        {
            using var stream = File.OpenRead(path);
            return ReadHdr(stream);
        }

        /// <summary>Reads Radiance RGBE (flat or new-style run-length encoded rows; -Y/+X orientation).</summary>
        public static HdrImage ReadHdr(Stream stream)
        {
            string ReadLine()
            {
                var sb = new StringBuilder();
                int c;
                while ((c = stream.ReadByte()) >= 0 && c != '\n') sb.Append((char)c);
                if (c < 0 && sb.Length == 0) throw new InvalidDataException("Unexpected end of HDR header.");
                return sb.ToString();
            }

            var first = ReadLine();
            if (!first.StartsWith("#?", StringComparison.Ordinal)) throw new InvalidDataException("Not a Radiance HDR file.");
            while (true)
            {
                var line = ReadLine();
                if (line.Length == 0) break;
                if (line.StartsWith("FORMAT=", StringComparison.Ordinal) && line != "FORMAT=32-bit_rle_rgbe")
                    throw new InvalidDataException("Only RGBE HDR files are supported.");
            }
            var size = ReadLine().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (size.Length != 4 || size[0] != "-Y" || size[2] != "+X") throw new InvalidDataException("Unsupported HDR orientation.");
            var height = int.Parse(size[1], CultureInfo.InvariantCulture);
            var width = int.Parse(size[3], CultureInfo.InvariantCulture);
            var image = new HdrImage(width, height);

            var row = new byte[width * 4];
            var channels = new byte[4][];
            for (var k = 0; k < 4; k++) channels[k] = new byte[width];
            for (var y = 0; y < height; y++)
            {
                var head = ReadExactly(stream, 4);
                if (width >= 8 && width < 32768 && head[0] == 2 && head[1] == 2 && (head[2] << 8 | head[3]) == width)
                {
                    // New-style RLE: each channel separately.
                    for (var k = 0; k < 4; k++)
                    {
                        var x = 0;
                        while (x < width)
                        {
                            var count = Next(stream);
                            if (count > 128)
                            {
                                count -= 128;
                                var value = (byte)Next(stream);
                                if (x + count > width) throw new InvalidDataException("Bad HDR run.");
                                Array.Fill(channels[k], value, x, count);
                                x += count;
                            }
                            else
                            {
                                if (count == 0 || x + count > width) throw new InvalidDataException("Bad HDR run.");
                                for (var i = 0; i < count; i++) channels[k][x++] = (byte)Next(stream);
                            }
                        }
                    }
                    for (var x = 0; x < width; x++)
                        image[x, y] = FromRgbe(channels[0][x], channels[1][x], channels[2][x], channels[3][x]);
                }
                else
                {
                    Array.Copy(head, row, 4);
                    var rest = ReadExactly(stream, row.Length - 4);
                    Array.Copy(rest, 0, row, 4, rest.Length);
                    for (var x = 0; x < width; x++)
                        image[x, y] = FromRgbe(row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]);
                }
            }
            return image;
        }

        private static int Next(Stream stream)
        {
            var b = stream.ReadByte();
            if (b < 0) throw new InvalidDataException("Unexpected end of HDR data.");
            return b;
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            stream.ReadExactly(buffer);
            return buffer;
        }

        private static (byte R, byte G, byte B, byte E) ToRgbe(Vector3 c)
        {
            c = Vector3.Max(c, Vector3.Zero);
            if (!float.IsFinite(c.X) || !float.IsFinite(c.Y) || !float.IsFinite(c.Z)) c = Vector3.Zero;
            var max = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
            if (max < 1e-32f) return (0, 0, 0, 0);
            var exponent = (int)MathF.Floor(MathF.Log2(max)) + 1;
            var scale = MathF.ScaleB(1f, -exponent) * 256f;
            // Guard against rounding up to 256 right at a power of two.
            if (max * scale >= 256f) { exponent++; scale *= 0.5f; }
            return ((byte)(c.X * scale), (byte)(c.Y * scale), (byte)(c.Z * scale), (byte)Math.Clamp(exponent + 128, 0, 255));
        }

        private static Vector3 FromRgbe(byte r, byte g, byte b, byte e)
        {
            if (e == 0) return Vector3.Zero;
            var f = MathF.ScaleB(1f, e - 136); // 2^(e-128) / 256
            return new Vector3((r + 0.5f) * f, (g + 0.5f) * f, (b + 0.5f) * f);
        }
    }
}
