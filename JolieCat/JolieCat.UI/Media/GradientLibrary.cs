using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JolieCat.UI.Media
{
    /// <summary>One stop of a saved gradient.</summary>
    public sealed record SavedStop(string Hex, double Location, double Opacity);

    /// <summary>A gradient the user saved as a preset.</summary>
    public sealed record SavedGradient(string Name, List<SavedStop> Stops);

    /// <summary>
    /// The user's saved gradient presets, shared by every document and kept in
    /// %AppData%\JolieCat\gradients.json.
    /// </summary>
    public sealed class GradientLibrary
    {
        private static GradientLibrary? _shared;
        private readonly string? _path;

        /// <param name="path">Where presets are saved; null keeps them in memory (tests).</param>
        public GradientLibrary(string? path)
        {
            _path = path;
            if (path is null || !File.Exists(path)) return;
            try
            {
                Gradients.AddRange(JsonSerializer.Deserialize<List<SavedGradient>>(File.ReadAllText(path)) ?? new());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // A broken file just means no saved presets.
            }
        }

        public static GradientLibrary Shared => _shared ??= new GradientLibrary(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat", "gradients.json"));

        /// <summary>Lets tests use an in-memory library.</summary>
        internal static void UseForTests(GradientLibrary library) => _shared = library;

        public List<SavedGradient> Gradients { get; } = new();

        public SavedGradient? Find(string name) => Gradients.FirstOrDefault(g => g.Name == name);

        public void Save(SavedGradient gradient)
        {
            Gradients.RemoveAll(g => g.Name == gradient.Name);
            Gradients.Add(gradient);
            Write();
        }

        public bool Delete(string name)
        {
            var removed = Gradients.RemoveAll(g => g.Name == name) > 0;
            if (removed) Write();
            return removed;
        }

        private void Write()
        {
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(Gradients, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
