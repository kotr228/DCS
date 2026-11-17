namespace GrayCat.UI.ViewModels;

using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using System.Text.RegularExpressions;

public class BlockViewModel : BaseViewModel
{
    private static readonly Regex HexColor = new(@"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$", RegexOptions.Compiled);
    private static readonly string[] SummaryKeys =
        { "headline", "title", "brandName", "text", "planName", "quote", "copyright", "caption", "videoUrl", "src" };

    private readonly BlockModel _model;
    private bool _isSelected;

    /// <summary>Raised whenever anything that ends up in the project file changes.</summary>
    public event EventHandler? Changed;

    public BlockViewModel(BlockModel model)
    {
        _model = model;
    }

    public BlockModel Model => _model;
    public string Id => _model.Id;
    public string Type => _model.Type;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Short text shown on the canvas card.</summary>
    public string DisplayText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_model.Content))
                return _model.Content;
            foreach (var key in SummaryKeys)
            {
                if (_model.Properties.TryGetValue(key, out var value))
                {
                    var text = PropertyValues.AsString(value);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Length > 120 ? text[..120] + "…" : text;
                }
            }
            return ListSummary();
        }
    }

    private static readonly string[] ItemTextKeys = { "title", "name", "question", "text", "label", "author", "alt", "platform" };

    /// <summary>Blocks made of lists (stats, footer links, plans…): "1000+ Clients · 50+ Projects · …".</summary>
    private string ListSummary()
    {
        foreach (var value in _model.Properties.Values)
        {
            if (PropertyValues.ToPlain(value) is not List<object?> list)
                continue;
            var parts = list.OfType<Dictionary<string, object?>>().Select(ItemText).Where(t => t.Length > 0).ToList();
            if (parts.Count == 0)
                continue;
            var text = string.Join(" · ", parts.Take(3)) + (parts.Count > 3 ? " · …" : "");
            return text.Length > 120 ? text[..120] + "…" : text;
        }
        return string.Empty;
    }

    private static string ItemText(Dictionary<string, object?> item)
    {
        string Get(string key) => item.TryGetValue(key, out var v) ? PropertyValues.AsString(v).Trim() : "";
        var number = Get("number");
        var label = Get("label");
        if (number.Length > 0)
            return label.Length > 0 ? $"{number} {label}" : number;
        return ItemTextKeys.Select(Get).FirstOrDefault(t => t.Length > 0 && !t.StartsWith("http")) ?? "";
    }

    public string Content
    {
        get => _model.Content;
        set => Update(() => _model.Content = value ?? string.Empty, nameof(Content), nameof(DisplayText));
    }

    public double PositionX
    {
        get => _model.PositionX;
        set => Update(() => _model.PositionX = Math.Max(0, value), nameof(PositionX));
    }

    public double PositionY
    {
        get => _model.PositionY;
        set => Update(() => _model.PositionY = Math.Max(0, value), nameof(PositionY));
    }

    public double Width
    {
        get => _model.Width;
        set => Update(() => _model.Width = Math.Clamp(value, 50, 2000), nameof(Width));
    }

    public double Height
    {
        get => _model.Height;
        set => Update(() => _model.Height = Math.Clamp(value, 30, 2000), nameof(Height));
    }

    public string Color
    {
        get => _model.Color;
        set { if (IsValidHexColor(value)) Update(() => _model.Color = value, nameof(Color)); }
    }

    public string Background
    {
        get => _model.Background;
        set { if (IsValidHexColor(value)) Update(() => _model.Background = value, nameof(Background)); }
    }

    public int FontSize
    {
        get => _model.FontSize;
        set { if (value is >= 8 and <= 72) Update(() => _model.FontSize = value, nameof(FontSize)); }
    }

    public string FontFamily
    {
        get => _model.FontFamily;
        set => Update(() => _model.FontFamily = value, nameof(FontFamily));
    }

    public string TextAlignment
    {
        get => _model.TextAlignment;
        set => Update(() => _model.TextAlignment = value, nameof(TextAlignment));
    }

    public string? ImagePath
    {
        get => _model.ImagePath;
        set => Update(() => _model.ImagePath = value, nameof(ImagePath));
    }

    public int BorderRadius
    {
        get => _model.BorderRadius;
        set => Update(() => _model.BorderRadius = Math.Clamp(value, 0, 200), nameof(BorderRadius));
    }

    public DateTime ModifiedAt => _model.ModifiedAt;

    /// <summary>The block is kept in the project but left out of the published site.</summary>
    public bool IsHiddenOnSite => PropertyValues.AsBool(GetBlockProperty("hidden"));

    public Dictionary<string, object> Properties => _model.Properties;
    public Dictionary<string, string> Styles => _model.Styles;

    public object? GetBlockProperty(string key) =>
        _model.Properties.TryGetValue(key, out var value) ? value : null;

    /// <summary>Sets a type-specific property and marks the block as changed.</summary>
    public void SetBlockProperty(string key, object value)
    {
        _model.SetProperty(key, value);
        OnPropertyChanged(nameof(Properties));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(IsHiddenOnSite));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ResetStyle()
    {
        Color = "#000000";
        Background = "#FFFFFF";
        FontSize = 14;
        TextAlignment = "Left";
    }

    public static bool IsValidHexColor(string? hex) => hex != null && HexColor.IsMatch(hex);

    private void Update(Action apply, params string[] properties)
    {
        apply();
        _model.ModifiedAt = DateTime.UtcNow;
        foreach (var name in properties)
            OnPropertyChanged(name);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
