using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JolieCat.Core.Actions
{
    /// <summary>
    /// One recorded step of an action (Photoshop's Actions panel): what to run
    /// (<see cref="Kind"/> + <see cref="Command"/>) and its settings as text.
    /// Kinds understood by the player: "Filter", "CameraRaw", "Adjustment", "Layers",
    /// "ImageSize". Unknown kinds are skipped, so old files keep loading.
    /// </summary>
    public sealed record ActionStep
    {
        public string Kind { get; init; } = string.Empty;

        /// <summary>Which command of that kind, e.g. the filter or Layers command name.</summary>
        public string Command { get; init; } = string.Empty;

        /// <summary>What the Actions panel shows, e.g. "Gaussian Blur (radius 5)".</summary>
        public string Title { get; init; } = string.Empty;

        public Dictionary<string, string> Args { get; init; } = new();

        public ActionStep With(string key, double value)
        {
            Args[key] = value.ToString("R", CultureInfo.InvariantCulture);
            return this;
        }

        public ActionStep With(string key, string value)
        {
            Args[key] = value;
            return this;
        }

        public double Number(string key, double fallback = 0) =>
            Args.TryGetValue(key, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        public string Text(string key, string fallback = "") => Args.TryGetValue(key, out var text) ? text : fallback;
    }

    /// <summary>A named list of steps that can be played back on any document.</summary>
    public sealed class RecordedAction
    {
        public string Name { get; set; } = "Action";

        public List<ActionStep> Steps { get; set; } = new();
    }

    /// <summary>Saves and loads the user's actions as JSON.</summary>
    public static class ActionLibrary
    {
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static string Serialize(IEnumerable<RecordedAction> actions) =>
            JsonSerializer.Serialize(actions.ToList(), Options);

        public static List<RecordedAction> Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<RecordedAction>();
            try
            {
                return JsonSerializer.Deserialize<List<RecordedAction>>(json, Options) ?? new List<RecordedAction>();
            }
            catch (JsonException)
            {
                return new List<RecordedAction>();
            }
        }

        public static List<RecordedAction> Load(string path)
        {
            try
            {
                return File.Exists(path) ? Deserialize(File.ReadAllText(path)) : new List<RecordedAction>();
            }
            catch (IOException)
            {
                return new List<RecordedAction>();
            }
            catch (UnauthorizedAccessException)
            {
                return new List<RecordedAction>();
            }
        }

        public static void Save(string path, IEnumerable<RecordedAction> actions)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, Serialize(actions));
        }
    }
}
