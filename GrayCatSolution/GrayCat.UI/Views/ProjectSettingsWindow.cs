namespace GrayCat.UI.Views;

using GrayCat.UI.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Shared.Models;

/// <summary>Edits the project's name, author, description and output type.</summary>
public class ProjectSettingsWindow : Controls.ThemedWindow
{
    private readonly TextBox _name;
    private readonly TextBox _author;
    private readonly TextBox _description;
    private readonly ComboBox _type;
    private readonly TextBlock _typeNote;
    private readonly ProjectType _originalType;
    private readonly TextBox _primary;
    private readonly TextBox _accent;
    private readonly ComboBox _font;

    public string ProjectName => _name.Text;
    public string Author => _author.Text;
    public string Description => _description.Text;
    public ProjectType ProjectType => (ProjectType)_type.SelectedItem;
    public string PrimaryColor => _primary.Text.Trim();
    public string AccentColor => _accent.Text.Trim();
    public string FontName => _font.SelectedItem as string ?? SiteThemes.DefaultFont;
    public string SiteLanguage => (_language.SelectedItem as KeyValuePair<string, string>?)?.Key ?? "en";
    public string Favicon => _favicon.Text.Trim();

    private readonly ComboBox _language;
    private readonly TextBox _favicon;

    public ProjectSettingsWindow(ProjectModel project, Func<string?>? pickImage = null)
    {
        Title = Loc.T("Project Settings");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _originalType = project.Type;

        var panel = new StackPanel { Margin = new Thickness(20) };

        _name = Field(panel, Loc.T("Site name (page title and brand)"), project.ProjectName);
        _author = Field(panel, Loc.T("Author"), project.Author);
        _description = Field(panel, Loc.T("Description (shown to search engines)"),
            project.Settings.TryGetValue(SettingKeys.Description, out var d) ? d : string.Empty, multiline: true);

        panel.Children.Add(Label(Loc.T("Project type")));
        var typeText = new FrameworkElementFactory(typeof(TextBlock));
        typeText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new ProjectTypeNameConverter() });
        _type = new ComboBox
        {
            ItemsSource = Enum.GetValues<ProjectType>(),
            SelectedItem = project.Type,
            ItemTemplate = new DataTemplate { VisualTree = typeText }
        };
        panel.Children.Add(_type);
        _typeNote = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 12
        };
        panel.Children.Add(_typeNote);
        _type.SelectionChanged += (_, _) => UpdateNote();
        UpdateNote();

        var theme = SiteThemes.From(project);
        panel.Children.Add(new TextBlock { Text = Loc.T("Theme"), FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 18, 0, 8) });
        _primary = ColorField(panel, Loc.T("Main colour (buttons, links, hero)"), theme.Primary);
        _accent = ColorField(panel, Loc.T("Accent colour (second colour of the hero gradient)"), theme.Accent);
        panel.Children.Add(Label(Loc.T("Font")));
        _font = new ComboBox { ItemsSource = SiteThemes.Fonts.Keys.ToList(), SelectedItem = theme.Font };
        panel.Children.Add(_font);
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Fonts other than System and Georgia load from Google Fonts when the site is opened."),
            Foreground = new SolidColorBrush(Color.FromRgb(143, 136, 132)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        panel.Children.Add(new TextBlock { Text = Loc.T("Site"), FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 18, 0, 8) });
        panel.Children.Add(Label(Loc.T("Language of the content (helps search engines and screen readers)")));
        var languages = SiteBuilder.Languages.ToList();
        var currentLanguage = project.Settings.TryGetValue(SettingKeys.Language, out var lang) ? lang : "en";
        _language = new ComboBox
        {
            ItemsSource = languages,
            DisplayMemberPath = "Value",
            SelectedItem = languages.FirstOrDefault(l => l.Key == currentLanguage) is { Key: not null } found ? found : languages[1],
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(_language);

        panel.Children.Add(Label(Loc.T("Browser tab icon (favicon)")));
        var iconRow = new DockPanel();
        _favicon = new TextBox { Text = project.Settings.TryGetValue(SettingKeys.Favicon, out var icon) ? icon : string.Empty, Padding = new Thickness(6) };
        if (pickImage != null)
        {
            var browse = new Button { Content = Loc.T("Browse…"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
            browse.Click += (_, _) =>
            {
                if (pickImage() is { } path)
                    _favicon.Text = path;
            };
            DockPanel.SetDock(browse, Dock.Right);
            iconRow.Children.Add(browse);
        }
        iconRow.Children.Add(_favicon);
        panel.Children.Add(iconRow);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = Loc.T("Save"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8) };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                ThemedMessageBox.Show(this, Loc.T("The site name cannot be empty."), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!SiteBuilder.IsColor(PrimaryColor) || !SiteBuilder.IsColor(AccentColor))
            {
                ThemedMessageBox.Show(this, Loc.T("Colours must look like #4F46E5."), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        };
        var cancel = new Button { Content = Loc.T("Cancel"), Width = 90, IsCancel = true, Padding = new Thickness(8) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) => _name.Focus();
    }

    private void UpdateNote()
    {
        _typeNote.Text = ProjectType == _originalType
            ? Loc.T("Changing the type makes Generate Code produce a different kind of project in the same folder.")
            : Loc.F("Next time you generate code, a {0} project is written into the project folder. ", ProjectType) +
              Loc.F("Files from the previous {0} project are left in place; delete them if you no longer need them.", _originalType);
    }

    private static TextBox ColorField(Panel panel, string label, string value)
    {
        panel.Children.Add(Label(label));
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var swatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(4), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) };
        var box = new TextBox { Text = value, Padding = new Thickness(6) };
        void Update()
        {
            try { swatch.Background = (Brush)new BrushConverter().ConvertFromString(box.Text.Trim())!; }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidOperationException) { swatch.Background = Brushes.Transparent; }
        }
        box.TextChanged += (_, _) => Update();
        Update();
        DockPanel.SetDock(swatch, Dock.Left);
        row.Children.Add(swatch);
        row.Children.Add(box);
        panel.Children.Add(row);
        return box;
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) };

    private static TextBox Field(Panel panel, string label, string value, bool multiline = false)
    {
        panel.Children.Add(Label(label));
        var box = new TextBox
        {
            Text = value,
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 14),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Height = multiline ? 70 : double.NaN
        };
        panel.Children.Add(box);
        return box;
    }
}
