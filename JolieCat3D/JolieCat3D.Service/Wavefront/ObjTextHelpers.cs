using System.Globalization;

namespace JolieCat3D.Service.Wavefront
{
    /// <summary>Small text-parsing/formatting helpers shared by <see cref="ObjImporter"/>,
    /// <see cref="ObjExporter"/>, and <see cref="MtlFile"/> - both the OBJ and MTL
    /// formats are plain whitespace-separated text, share the same "#" comment
    /// convention, and both need every number written/read culture-invariantly (a
    /// comma-decimal locale would otherwise silently corrupt every coordinate).</summary>
    internal static class ObjTextHelpers
    {
        public static string StripComment(string line)
        {
            var hashIndex = line.IndexOf('#');
            return hashIndex >= 0 ? line[..hashIndex] : line;
        }

        public static float ParseFloat(string token) => float.Parse(token, CultureInfo.InvariantCulture);

        public static string FormatFloat(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>OBJ/MTL object and material names can't contain whitespace (the
        /// format has no quoting) - replaced with underscores, and an empty result (an
        /// unnamed or all-whitespace name) falls back to <paramref name="fallback"/>
        /// rather than writing a blank token a reader would choke on.</summary>
        public static string SanitizeName(string name, string fallback)
        {
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (char.IsWhiteSpace(chars[i])) chars[i] = '_';

            var sanitized = new string(chars);
            return sanitized.Length == 0 ? fallback : sanitized;
        }
    }
}
