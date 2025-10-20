namespace GrayCat.UI.ViewModels;

using GrayCat.Core.Models;
using GrayCat.Core.Data;
using System.Text.RegularExpressions;

public class BlockViewModel : BaseViewModel
{
    private readonly BlockModel _model;
    private readonly DatabaseService? _databaseService;

    public BlockModel Model => _model;

    public BlockViewModel(BlockModel model, DatabaseService? databaseService = null)
    {
        _model = model;
        _databaseService = databaseService;
    }

    public string Id => _model.Id;

    public string Type
    {
        get => _model.Type;
        set
        {
            _model.Type = value;
            OnPropertyChanged();
            AutoSave();
        }
    }

    public string Content
    {
        get => _model.Content;
        set
        {
            _model.Content = value;
            _model.ModifiedAt = DateTime.UtcNow;
            OnPropertyChanged();
            AutoSave();
        }
    }

    public double PositionX
    {
        get => _model.PositionX;
        set
        {
            _model.PositionX = value;
            OnPropertyChanged();
            AutoSave();
        }
    }

    public double PositionY
    {
        get => _model.PositionY;
        set
        {
            _model.PositionY = value;
            OnPropertyChanged();
            AutoSave();
        }
    }

    public double Width
    {
        get => _model.Width;
        set
        {
            _model.Width = value;
            OnPropertyChanged();
            AutoSave();
        }
    }

    public double Height
    {
        get => _model.Height;
        set
        {
            _model.Height = value;
            OnPropertyChanged();
            AutoSave();
        }
    }

    // NEW in v0.3.2
    public string Color
    {
        get => _model.Color;
        set
        {
            if (IsValidHexColor(value))
            {
                _model.Color = value;
                _model.ModifiedAt = DateTime.UtcNow;
                OnPropertyChanged();
                AutoSave();
            }
        }
    }

    public string Background
    {
        get => _model.Background;
        set
        {
            if (IsValidHexColor(value))
            {
                _model.Background = value;
                _model.ModifiedAt = DateTime.UtcNow;
                OnPropertyChanged();
                AutoSave();
            }
        }
    }

    public int FontSize
    {
        get => _model.FontSize;
        set
        {
            if (value >= 8 && value <= 72)
            {
                _model.FontSize = value;
                _model.ModifiedAt = DateTime.UtcNow;
                OnPropertyChanged();
                AutoSave();
            }
        }
    }

    public string FontFamily
    {
        get => _model.FontFamily;
        set
        {
            _model.FontFamily = value;
            OnPropertyChanged();
            AutoSave();
        }
    }

    public string TextAlignment
    {
        get => _model.TextAlignment;
        set
        {
            _model.TextAlignment = value;
            OnPropertyChanged();
            AutoSave();
        }
    }

    public string? ImagePath
    {
        get => _model.ImagePath;
        set
        {
            _model.ImagePath = value;
            OnPropertyChanged();
            AutoSave();
        }
    }

    public int BorderRadius
    {
        get => _model.BorderRadius;
        set
        {
            _model.BorderRadius = value;
            OnPropertyChanged();
            AutoSave();
        }
    }

    public DateTime ModifiedAt => _model.ModifiedAt;

    public string CssClasses
    {
        get => _model.CssClasses;
        set
        {
            _model.CssClasses = value;
            OnPropertyChanged();
            AutoSave();
        }
    }

    public Dictionary<string, object> Properties => _model.Properties;
    public Dictionary<string, string> Styles => _model.Styles;

    // Validation
    private bool IsValidHexColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var pattern = @"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$";
        return Regex.IsMatch(hex, pattern);
    }

    // Auto-save to database
    private async void AutoSave()
    {
        if (_databaseService != null)
        {
            try
            {
                await _databaseService.SaveBlockAsync(_model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-save failed: {ex.Message}");
            }
        }
    }

    public void LoadPropertiesFromModel()
    {
        _model.LoadFromJson(); // extension method з DbContext
        OnPropertyChanged(nameof(Properties));
    }

    public async Task SavePropertiesToModel()
    {
        await Task.Run(() => AutoSave());
    }
}