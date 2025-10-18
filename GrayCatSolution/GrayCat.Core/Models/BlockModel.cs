namespace GrayCat.Core.Models;

public class BlockModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = "Text";
    public string Content { get; set; } = string.Empty;

    // Position
    public double PositionX { get; set; }
    public double PositionY { get; set; }

    // Size
    public double Width { get; set; } = 200;
    public double Height { get; set; } = 100;

    // Visual Properties (NEW in v0.3.2)
    public string Color { get; set; } = "#000000";
    public string Background { get; set; } = "#FFFFFF";
    public int FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Arial";
    public string TextAlignment { get; set; } = "Left";

    // Image Properties
    public string? ImagePath { get; set; }
    public int BorderRadius { get; set; } = 0;

    // Advanced
    public Dictionary<string, object> Properties { get; set; } = new();
    public string CssClasses { get; set; } = string.Empty;
    public Dictionary<string, string> Styles { get; set; } = new();

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}