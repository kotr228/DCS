using System.IO;
using System.Text.Json;

namespace AsmodayCat.UI.Services;

// UI-only preferences (not service settings), kept next to the service database:
// %LOCALAPPDATA%\CatSuite\AsmodayCat\ui-settings.json
public sealed class UiSettingsStore
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CatSuite", "AsmodayCat", "ui-settings.json");

    public UiSettingsStore() => Current = Load();

    public UiSettings Current { get; }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, _opts));
        }
        catch (IOException) { /* preferences are best-effort */ }
        catch (UnauthorizedAccessException) { }
    }

    private UiSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(_path)) ?? new UiSettings()
                : new UiSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UiSettings();
        }
    }
}

public sealed class UiSettings
{
    public bool ChatAllowWeb   { get; set; }
    public bool ChatAllowTools { get; set; }
    public bool VoiceReplies   { get; set; }
    public string? Language    { get; set; }   // uk / en / ru; empty = follow Windows on first run
}
