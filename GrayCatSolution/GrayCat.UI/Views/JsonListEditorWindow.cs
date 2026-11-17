namespace GrayCat.UI.Views;

using GrayCat.UI.Localization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

/// <summary>Small dialog for editing list properties (menu items, features, FAQ...) as JSON.</summary>
public class JsonListEditorWindow : Controls.ThemedWindow
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly TextBox _editor;
    private readonly TextBlock _error;

    /// <summary>Formatted JSON array after the user pressed Save.</summary>
    public string? Result { get; private set; }

    public JsonListEditorWindow(string title, string key, string currentJson, Func<string?>? pickImage = null)
    {
        Title = title;
        Width = 560;
        Height = 520;
        MinWidth = 400;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(16) };

        var hint = new TextBlock
        {
            Text = Loc.F("One entry per item, as a JSON list. Example:\n{0}", Example(key)),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(88, 96, 105)),
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(hint, Dock.Top);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var save = new Button { Content = Loc.T("Save"), Width = 90, IsDefault = false, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8) };
        var cancel = new Button { Content = Loc.T("Cancel"), Width = 90, IsCancel = true, Padding = new Thickness(8) };
        save.Click += (_, _) => TrySave();
        if (pickImage != null)
        {
            var insert = new Button { Content = Loc.T("Insert image…"), Width = 120, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8) };
            insert.Click += (_, _) =>
            {
                if (pickImage() is { } path)
                {
                    var text = JsonSerializer.Serialize(path);
                    _editor!.SelectedText = text;
                    _editor.CaretIndex = _editor.SelectionStart + text.Length;
                    _editor.Focus();
                }
            };
            buttons.Children.Add(insert);
        }
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);

        _error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(_error, Dock.Bottom);

        _editor = new TextBox
        {
            Text = Format(currentJson),
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        root.Children.Add(hint);
        root.Children.Add(buttons);
        root.Children.Add(_error);
        root.Children.Add(_editor);
        Content = root;
        Loaded += (_, _) => _editor.Focus();
    }

    private void TrySave()
    {
        try
        {
            using var doc = JsonDocument.Parse(_editor.Text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                _error.Text = Loc.T("The value must be a list: start with [ and end with ].");
                return;
            }
            Result = JsonSerializer.Serialize(doc.RootElement, Pretty);
            DialogResult = true;
        }
        catch (JsonException ex)
        {
            _error.Text = Loc.F("Invalid JSON (line {0}): {1}.", ex.LineNumber + 1, ex.Message.Split('.')[0]);
        }
    }

    private static string Format(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "[\n]";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, Pretty);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string Example(string key) => key switch
    {
        "features" => "[\"Unlimited projects\", \"Email support\"]",
        "items" => "[{ \"question\": \"How?\", \"answer\": \"Like this.\" }]",
        "stats" => "[{ \"number\": \"100+\", \"label\": \"Clients\" }]",
        "images" => "[{ \"src\": \"https://...\", \"alt\": \"Photo\", \"caption\": \"\" }]",
        "products" => "[{ \"name\": \"Mug\", \"price\": \"12\", \"description\": \"...\", \"image\": \"https://...\", \"url\": \"#\" }]",
        "fields" => "[{ \"name\": \"email\", \"type\": \"email\", \"label\": \"Email\", \"required\": true }]\ntypes: text, email, tel, number, url, date, textarea",
        "links" => "[{ \"text\": \"Home\", \"url\": \"#\" }]   or for social links:\n[{ \"platform\": \"instagram\", \"url\": \"https://instagram.com/...\" }]",
        _ => "[{ \"text\": \"Home\", \"url\": \"#\" }]"
    };
}
