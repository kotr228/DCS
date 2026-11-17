using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace JolieCat.UI.Media
{
    /// <summary>
    /// The user's own keyboard shortcuts (Edit &gt; Keyboard Shortcuts), kept in
    /// %APPDATA%\JolieCat\shortcuts.json as "command id" → "Ctrl+Shift+K" (empty: none).
    /// Only changed ones are stored; the rest keep their built-in keys.
    /// </summary>
    public static class ShortcutStore
    {
        public static string FilePath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat", "shortcuts.json");

        public static Dictionary<string, string> Load()
        {
            try
            {
                if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) is { } saved)
                    return saved;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
            return new Dictionary<string, string>();
        }

        public static void Save(Dictionary<string, string> overrides)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(overrides, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
