using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;
using static JolieCat3D.Service.Wavefront.ObjTextHelpers;

namespace JolieCat3D.Service.Wavefront
{
    /// <summary>
    /// Reads/writes a Wavefront <c>.mtl</c> material library - the companion file an
    /// <c>.obj</c>'s own <c>mtllib</c>/<c>usemtl</c> lines reference, since OBJ's own
    /// <c>f</c> face lines carry no color information at all. Only the handful of
    /// properties <see cref="Material"/> itself has: <c>Kd</c> (diffuse), <c>Ks</c>
    /// (specular), <c>Ns</c> (specular exponent/power), <c>d</c> (opacity) - not the
    /// dozens of other properties the MTL spec allows (texture maps, illumination
    /// models, ...), matching <see cref="Material"/>'s own "just enough for a standard
    /// diffuse+specular model" scope.
    /// </summary>
    internal static class MtlFile
    {
        public static IEnumerable<(string Name, Material Material)> Load(string path)
        {
            string? currentName = null;
            Material? current = null;

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "newmtl":
                        if (currentName is not null && current is not null) yield return (currentName, current);
                        currentName = parts.Length > 1 ? parts[1] : "Material";
                        current = new Material(currentName);
                        break;

                    case "Kd" when current is not null && parts.Length >= 4:
                        current.DiffuseColor = new Color4(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3]));
                        break;

                    case "Ks" when current is not null && parts.Length >= 4:
                        current.SpecularColor = new Color4(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3]));
                        break;

                    case "Ns" when current is not null && parts.Length >= 2:
                        current.SpecularPower = ParseFloat(parts[1]);
                        break;

                    // "d" (dissolve/opacity, 1 = opaque) and its common alternate spelling
                    // "Tr" (transparency, the inverse - 0 = opaque) both appear in the wild.
                    case "d" when current is not null && parts.Length >= 2:
                        current.Opacity = ParseFloat(parts[1]);
                        break;

                    case "Tr" when current is not null && parts.Length >= 2:
                        current.Opacity = 1f - ParseFloat(parts[1]);
                        break;
                }
            }

            if (currentName is not null && current is not null) yield return (currentName, current);
        }

        public static void Save(string path, IReadOnlyList<(string Name, Material Material)> materials)
        {
            using var writer = new StreamWriter(path, append: false);
            writer.WriteLine("# Exported by JolieCat3D");

            foreach (var (name, material) in materials)
            {
                writer.WriteLine();
                writer.WriteLine($"newmtl {name}");
                writer.WriteLine($"Kd {FormatFloat(material.DiffuseColor.R)} {FormatFloat(material.DiffuseColor.G)} {FormatFloat(material.DiffuseColor.B)}");
                writer.WriteLine($"Ks {FormatFloat(material.SpecularColor.R)} {FormatFloat(material.SpecularColor.G)} {FormatFloat(material.SpecularColor.B)}");
                writer.WriteLine($"Ns {FormatFloat((float)material.SpecularPower)}");
                writer.WriteLine($"d {FormatFloat(material.Opacity)}");
            }
        }
    }
}
