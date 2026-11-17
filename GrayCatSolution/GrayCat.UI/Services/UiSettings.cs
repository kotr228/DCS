namespace GrayCat.UI.Services;

using System.IO;
using System.Text.Json;
using GrayCat.Core.Constants;

/// <summary>Editor preferences kept between runs (%APPDATA%\CatSuite\GrayCat\settings.json).</summary>
public sealed class UiSettings
{
    public string? Language { get; set; }

    /// <summary>Main window size and position (restored bounds) and whether it was maximized.</summary>
    public WindowPlacement? MainWindow { get; set; }

    private static string FilePath => Path.Combine(AppConstants.Paths.AppData, "settings.json");

    public static UiSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(FilePath)) ?? new UiSettings()
                : new UiSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UiSettings();
        }
    }

    public sealed class WindowPlacement
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppConstants.Paths.AppData);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; never fail because of them.
        }
    }
}
