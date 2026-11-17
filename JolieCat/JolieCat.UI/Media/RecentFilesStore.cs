using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JolieCat.UI.Media
{
    /// <summary>The File &gt; Open Recent list, kept as one path per line in
    /// %APPDATA%\JolieCat\recent.txt. Failures to read or write are ignored - the list
    /// is a convenience, never worth an error dialog.</summary>
    public static class RecentFilesStore
    {
        /// <summary>How many are kept (Edit &gt; Preferences).</summary>
        public static int MaxEntries { get; set; } = Preferences.Current.RecentFiles;

        private static string StorePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat", "recent.txt");

        public static IReadOnlyList<string> Load()
        {
            try
            {
                return File.Exists(StorePath)
                    ? File.ReadAllLines(StorePath).Where(line => !string.IsNullOrWhiteSpace(line)).Take(MaxEntries).ToList()
                    : new List<string>();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        public static void Save(IEnumerable<string> paths)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllLines(StorePath, paths.Take(MaxEntries));
            }
            catch (Exception)
            {
                // Read-only profile, disk full... the list just won't persist.
            }
        }

        /// <summary>Returns <paramref name="paths"/> with <paramref name="path"/> moved
        /// (or added) to the front, without duplicates, capped at <see cref="MaxEntries"/>.</summary>
        public static List<string> Promote(IEnumerable<string> paths, string path) =>
            new[] { path }
                .Concat(paths.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
                .Take(MaxEntries)
                .ToList();
    }
}
