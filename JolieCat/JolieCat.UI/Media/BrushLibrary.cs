using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JolieCat.UI.Media
{
    /// <summary>A brush preset: tip and dynamics settings.</summary>
    public sealed record BrushPreset(
        string Name, double Size, double Hardness, double Spacing, double Flow,
        double Angle, double Roundness, double SizeJitter, double Scatter);

    /// <summary>
    /// Brush presets: a few built in, plus the user's own (saved in
    /// %AppData%\JolieCat\brushes.json). Shared by every painting tool.
    /// </summary>
    public sealed class BrushLibrary
    {
        public static IReadOnlyList<BrushPreset> BuiltIn { get; } = new[]
        {
            new BrushPreset("Hard Round", 20, 100, 10, 100, 0, 100, 0, 0),
            new BrushPreset("Soft Round", 60, 0, 10, 100, 0, 100, 0, 0),
            new BrushPreset("Airbrush", 120, 0, 5, 8, 0, 100, 0, 0),
            new BrushPreset("Calligraphy", 30, 100, 2, 100, 45, 20, 0, 0),
            new BrushPreset("Chalk", 30, 60, 20, 60, 0, 70, 50, 40),
            new BrushPreset("Spray", 6, 100, 30, 100, 0, 100, 60, 400),
        };

        private static BrushLibrary? _shared;
        private readonly string? _path;

        public BrushLibrary(string? path)
        {
            _path = path;
            if (path is null || !File.Exists(path)) return;
            try
            {
                Saved.AddRange(JsonSerializer.Deserialize<List<BrushPreset>>(File.ReadAllText(path)) ?? new());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        public static BrushLibrary Shared => _shared ??= new BrushLibrary(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat", "brushes.json"));

        internal static void UseForTests(BrushLibrary library) => _shared = library;

        public List<BrushPreset> Saved { get; } = new();

        public IEnumerable<BrushPreset> All => BuiltIn.Concat(Saved);

        public BrushPreset? Find(string name) => All.FirstOrDefault(p => p.Name == name);

        public void Save(BrushPreset preset)
        {
            Saved.RemoveAll(p => p.Name == preset.Name);
            Saved.Add(preset);
            Write();
        }

        public bool Delete(string name)
        {
            var removed = Saved.RemoveAll(p => p.Name == name) > 0;
            if (removed) Write();
            return removed;
        }

        private void Write()
        {
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(Saved, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
