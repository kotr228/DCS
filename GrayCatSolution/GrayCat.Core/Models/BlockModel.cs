namespace GrayCat.Core.Models;

using System.ComponentModel.DataAnnotations.Schema;

public class BlockModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = "Text";

    /// <summary>Slug of the page this block is on ("" = home page).</summary>
    public string Page { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    // Position
    public double PositionX { get; set; }
    public double PositionY { get; set; }

    // Size
    public double Width { get; set; } = 200;
    public double Height { get; set; } = 100;

    // Visual Properties (v0.3.2)
    public string Color { get; set; } = "#000000";
    public string Background { get; set; } = "#FFFFFF";
    public int FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Arial";
    public string TextAlignment { get; set; } = "Left";

    // Image Properties
    public string? ImagePath { get; set; }
    public int BorderRadius { get; set; } = 0;

    // NEW in v0.3.4: JSON storage for dynamic properties
    public string PropertiesJson { get; set; } = "{}";
    public string StylesJson { get; set; } = "{}";
    public string BlockVersion { get; set; } = "0.3.4";

    // Advanced (not stored in DB, loaded from JSON)
    [NotMapped]
    public Dictionary<string, object> Properties { get; set; } = new();

    [NotMapped]
    public string CssClasses { get; set; } = string.Empty;

    [NotMapped]
    public Dictionary<string, string> Styles { get; set; } = new();

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Get a property value with type casting
    /// </summary>
    public T? GetProperty<T>(string key, T? defaultValue = default)
    {
        if (!Properties.ContainsKey(key))
            return defaultValue;

        try
        {
            var value = Properties[key];

            if (value is T typedValue)
                return typedValue;

            // Try to convert
            if (value != null)
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
        }
        catch
        {
            // Conversion failed, return default
        }

        return defaultValue;
    }

    /// <summary>
    /// Set a property value
    /// </summary>
    public void SetProperty(string key, object value)
    {
        Properties[key] = value;
        ModifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Check if property exists
    /// </summary>
    public bool HasProperty(string key)
    {
        return Properties.ContainsKey(key);
    }

    /// <summary>
    /// Remove a property
    /// </summary>
    public bool RemoveProperty(string key)
    {
        if (Properties.ContainsKey(key))
        {
            Properties.Remove(key);
            ModifiedAt = DateTime.UtcNow;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Get all property keys
    /// </summary>
    public IEnumerable<string> GetPropertyKeys()
    {
        return Properties.Keys;
    }

    /// <summary>
    /// Clone this block
    /// </summary>
    public BlockModel Clone()
    {
        return new BlockModel
        {
            Id = Guid.NewGuid().ToString(),
            Type = this.Type,
            Page = this.Page,
            Content = this.Content,
            PositionX = this.PositionX + 20,
            PositionY = this.PositionY + 20,
            Width = this.Width,
            Height = this.Height,
            Color = this.Color,
            Background = this.Background,
            FontSize = this.FontSize,
            FontFamily = this.FontFamily,
            TextAlignment = this.TextAlignment,
            ImagePath = this.ImagePath,
            BorderRadius = this.BorderRadius,
            Properties = new Dictionary<string, object>(this.Properties),
            Styles = new Dictionary<string, string>(this.Styles),
            CssClasses = this.CssClasses,
            BlockVersion = this.BlockVersion,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Initialize default properties based on block type (lists are stored as JSON text for editing).
    /// </summary>
    /// <param name="language">Site language for the starter texts ("uk" or "en").</param>
    public void InitializeDefaultProperties(string? language = null)
    {
        foreach (var kvp in GrayCat.Core.Generation.BlockDefaults.For(Type, language))
        {
            if (!Properties.ContainsKey(kvp.Key))
            {
                Properties[kvp.Key] = GrayCat.Core.Generation.PropertyValues.ToEditable(kvp.Value);
            }
        }
    }

    /// <summary>
    /// Validate this block
    /// </summary>
    public (bool IsValid, List<string> Errors) Validate()
    {
        var errors = new List<string>();

        // Basic validations
        if (string.IsNullOrWhiteSpace(Type))
            errors.Add("Block type is required");

        if (Width < 50 || Width > 2000)
            errors.Add("Width must be between 50 and 2000 pixels");

        if (Height < 30 || Height > 2000)
            errors.Add("Height must be between 30 and 2000 pixels");

        // Type-specific validations can be added here

        return (!errors.Any(), errors);
    }
}