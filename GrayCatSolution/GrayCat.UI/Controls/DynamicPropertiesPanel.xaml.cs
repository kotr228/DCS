namespace GrayCat.UI.Controls;

using GrayCat.UI.Localization;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using GrayCat.Core.Generation;
using GrayCat.Core.Validation;
using GrayCat.UI.ViewModels;
using GrayCat.UI.Views;

/// <summary>Right-hand panel that builds editors for the selected block.</summary>
public partial class DynamicPropertiesPanel : UserControl
{
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(125, 95, 67)); // logo brown
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(108, 117, 125));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(179, 54, 43));

    private MainViewModel? _viewModel;

    /// <summary>Combo items keep their real values (stored in the project) but show translated text.</summary>
    private static readonly DataTemplate ValueTemplate = CreateValueTemplate();

    private static DataTemplate CreateValueTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new TrValueConverter() });
        var template = new DataTemplate { VisualTree = text };
        template.Seal();
        return template;
    }

    public DynamicPropertiesPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Labels are built in code, so rebuild them in the new language. Only while on screen:
        // Loc lives for the whole app, and a closed window must not stay subscribed (leak, wrong thread).
        Loaded += (_, _) => Loc.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
        UpdatePropertiesPanel(null);
    }

    private void OnLanguageChanged()
    {
        if (Dispatcher.CheckAccess())
            UpdatePropertiesPanel(_viewModel?.SelectedBlock);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as MainViewModel;
        if (_viewModel != null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        UpdatePropertiesPanel(_viewModel?.SelectedBlock);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedBlock))
            UpdatePropertiesPanel(_viewModel?.SelectedBlock);
    }

    private void UpdatePropertiesPanel(BlockViewModel? block)
    {
        PropertiesContainer.Children.Clear();

        if (block == null)
        {
            PropertiesContainer.Children.Add(new TextBlock
            {
                Text = Loc.T("Select a block on the canvas to edit it,\nor drag a new one from the library."),
                FontSize = 13,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 20, 0, 0),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        PropertiesContainer.Children.Add(new TextBlock
        {
            Text = Loc.F("{0} block", Loc.Block(block.Type)),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        AddSection(Loc.T("Content"));
        AddTypeSpecificProperties(block);

        AddSection(Loc.T("On the page"));
        AddPropertyField(Loc.T("Section id (menu links like #contact jump here)"), "anchor", block);
        AddPropertyCheckBox(Loc.T("Hide on the published site"), "hidden", block);

        AddSection(Loc.T("Appearance"));
        AddColorField(Loc.T("Text color"), block, nameof(BlockViewModel.Color));
        AddColorField(Loc.T("Background"), block, nameof(BlockViewModel.Background));
        AddBoundSlider(Loc.T("Font size"), block, nameof(BlockViewModel.FontSize), 8, 72);
        AddBoundCombo(Loc.T("Text alignment"), block, nameof(BlockViewModel.TextAlignment), new[] { "Left", "Center", "Right", "Justify" });
        AddBoundCombo(Loc.T("Font"), block, nameof(BlockViewModel.FontFamily),
            new[] { "Arial", "Segoe UI", "Georgia", "Verdana", "Tahoma", "Trebuchet MS", "Times New Roman", "Courier New" });
        AddBoundSlider(Loc.T("Corner radius"), block, nameof(BlockViewModel.BorderRadius), 0, 48);

        AddSection(Loc.T("Canvas position (sets the order on the page)"));
        AddNumberPair(block, ("X", nameof(BlockViewModel.PositionX)), ("Y", nameof(BlockViewModel.PositionY)));
        AddNumberPair(block, (Loc.T("Width"), nameof(BlockViewModel.Width)), (Loc.T("Height"), nameof(BlockViewModel.Height)));

        AddActionButtons(block);
    }

    private void AddTypeSpecificProperties(BlockViewModel block)
    {
        switch (block.Type)
        {
            case "Text":
                AddBoundText(Loc.T("Text"), block, nameof(BlockViewModel.Content), multiline: true);
                break;

            case "Header":
                AddPropertyField(Loc.T("Title"), "title", block);
                AddPropertyField(Loc.T("Subtitle"), "subtitle", block, multiline: true);
                AddPropertyCheckBox(Loc.T("Show navigation links"), "showNavigation", block);
                AddPropertyCheckBox(Loc.T("List the site's pages in the menu"), "pagesInMenu", block, defaultValue: true);
                AddListEditor(Loc.T("Edit navigation links"), "links", block);
                break;

            case "Navbar":
                AddPropertyField(Loc.T("Brand name"), "brandName", block);
                AddPropertyCheckBox(Loc.T("List the site's pages in the menu"), "pagesInMenu", block, defaultValue: true);
                AddAssetField(Loc.T("Logo"), "logo", block);
                AddPropertyComboBox(Loc.T("Position"), "position", block, new[] { "static", "fixed", "sticky" });
                AddPropertyCheckBox(Loc.T("Transparent"), "transparent", block);
                AddPropertyCheckBox(Loc.T("Show search"), "showSearch", block);
                AddPropertyField(Loc.T("Button text (optional)"), "ctaText", block);
                AddPropertyField(Loc.T("Button URL"), "ctaUrl", block);
                AddListEditor(Loc.T("Edit menu items"), "menuItems", block);
                break;

            case "Hero":
                AddPropertyField(Loc.T("Headline"), "headline", block);
                AddPropertyField(Loc.T("Subheadline"), "subheadline", block, multiline: true);
                AddPropertyField(Loc.T("Button text"), "ctaText", block);
                AddPropertyField(Loc.T("Button URL"), "ctaUrl", block);
                AddAssetField(Loc.T("Background image"), "backgroundImage", block);
                AddPropertyCheckBox(Loc.T("Dark overlay on image"), "overlay", block);
                AddPropertySlider(Loc.T("Overlay opacity"), "overlayOpacity", block, 0, 1, 0.1);
                AddPropertyComboBox(Loc.T("Alignment"), "alignment", block, new[] { "left", "center", "right" });
                AddPropertyComboBox(Loc.T("Height"), "height", block, new[] { "small", "medium", "large", "full" });
                break;

            case "Button":
                AddPropertyField(Loc.T("Button text"), "text", block);
                AddPropertyField("URL", "url", block);
                AddPropertyComboBox(Loc.T("Variant"), "variant", block, new[] { "primary", "secondary", "success", "danger", "warning", "info" });
                AddPropertyComboBox(Loc.T("Size"), "size", block, new[] { "small", "medium", "large" });
                AddPropertyComboBox(Loc.T("Open in"), "target", block, new[] { "_self", "_blank" });
                AddPropertyCheckBox(Loc.T("Full width"), "fullWidth", block);
                AddPropertyCheckBox(Loc.T("Disabled"), "disabled", block);
                break;

            case "Card":
                AddPropertyField(Loc.T("Title"), "title", block);
                AddPropertyField(Loc.T("Description"), "description", block, multiline: true);
                AddAssetField(Loc.T("Image"), "image", block);
                AddPropertyComboBox(Loc.T("Image position"), "imagePosition", block, new[] { "top", "left", "right", "bottom" });
                AddPropertyField(Loc.T("Link text"), "linkText", block);
                AddPropertyField(Loc.T("Link URL"), "linkUrl", block);
                AddPropertyCheckBox(Loc.T("Shadow"), "shadow", block);
                AddPropertyCheckBox(Loc.T("Border"), "bordered", block);
                AddPropertyCheckBox(Loc.T("Lift on hover"), "hoverable", block);
                break;

            case "Image":
                AddAssetField(Loc.T("Image"), "src", block);
                AddPropertyField(Loc.T("Alt text"), "alt", block);
                AddPropertyField(Loc.T("Caption"), "caption", block);
                break;

            case "Gallery":
                AddPropertyField(Loc.T("Title"), "title", block);
                AddPropertySlider(Loc.T("Columns"), "columns", block, 1, 4, 1);
                AddListEditor(Loc.T("Edit images"), "images", block);
                break;

            case "ProductGrid":
                AddPropertyField(Loc.T("Title"), "title", block);
                AddPropertyField(Loc.T("Currency"), "currency", block);
                AddPropertyField(Loc.T("Button text"), "buttonText", block);
                AddPropertySlider(Loc.T("Columns"), "columns", block, 1, 4, 1);
                AddListEditor(Loc.T("Edit products"), "products", block);
                break;

            case "Form":
            case "ContactForm":
                AddPropertyField(Loc.T("Title"), "title", block);
                AddPropertyField(Loc.T("Subtitle"), "subtitle", block);
                AddPropertyField(Loc.T("Submit button text"), "submitText", block);
                AddPropertyField(Loc.T("Submit URL"), "submitUrl", block);
                AddPropertyField(Loc.T("Success message"), "successMessage", block, multiline: true);
                AddPropertyField(Loc.T("Error message"), "errorMessage", block, multiline: true);
                if (block.Type == "ContactForm")
                {
                    AddPropertyCheckBox(Loc.T("Add phone field"), "showPhoneField", block);
                    AddPropertyCheckBox(Loc.T("Add subject field"), "showSubjectField", block);
                }
                AddListEditor(Loc.T("Edit form fields"), "fields", block);
                break;

            case "Video":
                AddAssetField(Loc.T("Video (YouTube, Vimeo link or a video file)"), "videoUrl", block, video: true);
                AddPropertyComboBox(Loc.T("Aspect ratio"), "aspectRatio", block, new[] { "16:9", "4:3", "1:1", "9:16" });
                AddPropertyCheckBox(Loc.T("Autoplay (muted)"), "autoplay", block);
                AddPropertyCheckBox(Loc.T("Show controls"), "controls", block);
                AddPropertyCheckBox(Loc.T("Loop"), "loop", block);
                AddPropertyCheckBox(Loc.T("Muted"), "muted", block);
                break;

            case "Testimonial":
                AddPropertyField(Loc.T("Quote"), "quote", block, multiline: true);
                AddPropertyField(Loc.T("Author"), "author", block);
                AddPropertyField(Loc.T("Author role"), "role", block);
                AddAssetField(Loc.T("Avatar"), "avatar", block);
                AddPropertySlider(Loc.T("Rating"), "rating", block, 0, 5, 1);
                AddPropertyCheckBox(Loc.T("Show rating"), "showRating", block);
                AddPropertyComboBox(Loc.T("Layout"), "layout", block, new[] { "default", "card", "minimal" });
                break;

            case "Pricing":
                AddPropertyField(Loc.T("Plan name"), "planName", block);
                AddPropertyField(Loc.T("Price"), "price", block);
                AddPropertyField(Loc.T("Currency"), "currency", block);
                AddPropertyField(Loc.T("Period"), "period", block);
                AddPropertyField(Loc.T("Button text"), "ctaText", block);
                AddPropertyField(Loc.T("Button URL"), "ctaUrl", block);
                AddPropertyField(Loc.T("Badge (optional)"), "badge", block);
                AddPropertyCheckBox(Loc.T("Highlighted"), "highlighted", block);
                AddListEditor(Loc.T("Edit features"), "features", block);
                break;

            case "Accordion":
                AddPropertyField(Loc.T("Title"), "title", block);
                AddPropertyCheckBox(Loc.T("Allow several open at once"), "allowMultiple", block);
                AddPropertySlider(Loc.T("Open by default (item #, 0 = first)"), "defaultOpen", block, -1, 10, 1);
                AddListEditor(Loc.T("Edit questions"), "items", block);
                break;

            case "Stats":
                AddPropertyComboBox(Loc.T("Layout"), "layout", block, new[] { "horizontal", "vertical", "grid" });
                AddListEditor(Loc.T("Edit statistics"), "stats", block);
                break;

            case "SocialLinks":
                AddPropertyComboBox(Loc.T("Style"), "style", block, new[] { "icons", "buttons", "text" });
                AddPropertyComboBox(Loc.T("Size"), "size", block, new[] { "small", "medium", "large" });
                AddPropertyComboBox(Loc.T("Alignment"), "alignment", block, new[] { "left", "center", "right" });
                AddListEditor(Loc.T("Edit links"), "links", block);
                break;

            case "Divider":
                AddPropertyComboBox(Loc.T("Line style"), "style", block, new[] { "solid", "dashed", "dotted", "double" });
                AddPropertySlider(Loc.T("Thickness"), "thickness", block, 1, 10, 1);
                AddPropertyField(Loc.T("Line color (#RRGGBB)"), "color", block);
                AddPropertyComboBox(Loc.T("Spacing"), "spacing", block, new[] { "small", "medium", "large" });
                AddPropertyCheckBox(Loc.T("Show icon in the middle"), "withIcon", block);
                AddPropertyField(Loc.T("Icon"), "icon", block);
                break;

            case "Footer":
                AddPropertyField(Loc.T("Copyright text (empty = automatic)"), "copyright", block);
                AddListEditor(Loc.T("Edit footer links"), "links", block);
                break;

            default:
                AddBoundText(Loc.T("Text"), block, nameof(BlockViewModel.Content), multiline: true);
                break;
        }
    }

    // ========== Editors for type-specific properties ==========

    private void AddPropertyField(string label, string key, BlockViewModel block, bool multiline = false)
    {
        AddLabel(label);
        var textBox = CreateTextBox(multiline);
        textBox.Text = PropertyValues.AsString(block.GetBlockProperty(key));
        textBox.TextChanged += (_, _) => block.SetBlockProperty(key, textBox.Text);
        PropertiesContainer.Children.Add(textBox);
    }

    /// <summary>Text field for a URL plus a Browse button that copies a local file into the project's assets folder.</summary>
    private void AddAssetField(string label, string key, BlockViewModel block, bool video = false)
    {
        AddLabel(label);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textBox = new TextBox { Text = PropertyValues.AsString(block.GetBlockProperty(key)), ToolTip = Loc.T("Web address (https://...) or a file from the project's assets folder") };
        textBox.TextChanged += (_, _) => block.SetBlockProperty(key, textBox.Text);

        var browse = new Button { Content = Loc.T("Browse…"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        browse.Click += (_, _) =>
        {
            if (PickAsset(video) is { } path)
                textBox.Text = path; // TextChanged stores it in the block
        };

        Grid.SetColumn(browse, 1);
        grid.Children.Add(textBox);
        grid.Children.Add(browse);
        PropertiesContainer.Children.Add(grid);
    }

    /// <summary>Lets the user pick a file and imports it; returns "assets/..." or null.</summary>
    private string? PickAsset(bool video = false)
    {
        if (_viewModel?.WorkFolder == null)
            return null;

        var extensions = video ? ProjectAssets.VideoExtensions : ProjectAssets.ImageExtensions;
        var pattern = string.Join(";", extensions.Select(e => "*" + e));
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = video ? Loc.T("Choose a video") : Loc.T("Choose an image"),
            Filter = $"{Loc.T(video ? "Videos" : "Images")} ({pattern})|{pattern}|{Loc.T("All files")} (*.*)|*.*"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return null;

        try
        {
            return _viewModel.ImportAsset(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(Loc.F("Could not copy the file into the project:\n\n{0}", ex.Message), "GrayCat Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private void AddPropertyComboBox(string label, string key, BlockViewModel block, string[] options)
    {
        AddLabel(label);
        var comboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 12), ItemsSource = options, ItemTemplate = ValueTemplate };
        var current = PropertyValues.AsString(block.GetBlockProperty(key));
        comboBox.SelectedItem = options.FirstOrDefault(o => o.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is string value)
                block.SetBlockProperty(key, value);
        };
        PropertiesContainer.Children.Add(comboBox);
    }

    private void AddPropertyCheckBox(string label, string key, BlockViewModel block, bool defaultValue = false)
    {
        var checkBox = new CheckBox
        {
            Content = label,
            Margin = new Thickness(0, 0, 0, 10),
            IsChecked = PropertyValues.AsBool(block.GetBlockProperty(key), defaultValue)
        };
        checkBox.Checked += (_, _) => block.SetBlockProperty(key, true);
        checkBox.Unchecked += (_, _) => block.SetBlockProperty(key, false);
        PropertiesContainer.Children.Add(checkBox);
    }

    private void AddPropertySlider(string label, string key, BlockViewModel block, double min, double max, double step)
    {
        AddLabel(label);
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            Value = Math.Clamp(PropertyValues.AsDouble(block.GetBlockProperty(key), min), min, max)
        };
        var valueLabel = new TextBlock
        {
            Text = FormatNumber(slider.Value, step),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 11,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 0, 12)
        };
        slider.ValueChanged += (_, _) =>
        {
            block.SetBlockProperty(key, Math.Round(slider.Value, 2));
            valueLabel.Text = FormatNumber(slider.Value, step);
        };
        PropertiesContainer.Children.Add(slider);
        PropertiesContainer.Children.Add(valueLabel);
    }

    private void AddListEditor(string buttonText, string key, BlockViewModel block)
    {
        var count = PropertyValues.AsList(block.GetBlockProperty(key)).Count;
        var button = new Button
        {
            Content = $"{buttonText} ({count})",
            Margin = new Thickness(0, 4, 0, 12),
            Padding = new Thickness(10, 6, 10, 6)
        };
        button.Click += (_, _) =>
        {
            var editor = new JsonListEditorWindow(buttonText, key, PropertyValues.AsString(block.GetBlockProperty(key)),
                key is "images" or "products" ? () => PickAsset() : null)
            {
                Owner = Window.GetWindow(this)
            };
            if (editor.ShowDialog() == true && editor.Result != null)
            {
                block.SetBlockProperty(key, editor.Result);
                button.Content = $"{buttonText} ({PropertyValues.AsList(editor.Result).Count})";
            }
        };
        PropertiesContainer.Children.Add(button);
    }

    // ========== Editors bound to block view-model properties ==========

    private void AddBoundText(string label, BlockViewModel block, string path, bool multiline)
    {
        AddLabel(label);
        var textBox = CreateTextBox(multiline);
        if (multiline) textBox.Height = 110;
        textBox.SetBinding(TextBox.TextProperty, new Binding(path)
        {
            Source = block,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        PropertiesContainer.Children.Add(textBox);
    }

    private void AddColorField(string label, BlockViewModel block, string path)
    {
        AddLabel(label);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var swatch = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        swatch.SetBinding(Border.BackgroundProperty, new Binding(path) { Source = block, Converter = new Converters.HexColorConverter() });

        var textBox = new TextBox { Text = (string)typeof(BlockViewModel).GetProperty(path)!.GetValue(block)! };
        textBox.TextChanged += (_, _) =>
        {
            var value = textBox.Text.Trim();
            var valid = BlockViewModel.IsValidHexColor(value);
            textBox.BorderBrush = valid ? SystemColors.ControlDarkBrush : ErrorBrush;
            textBox.ToolTip = valid ? null : Loc.T("Use #RRGGBB, for example #1E293B");
            if (valid)
                typeof(BlockViewModel).GetProperty(path)!.SetValue(block, value);
        };
        Grid.SetColumn(swatch, 0);
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(swatch);
        grid.Children.Add(textBox);
        PropertiesContainer.Children.Add(grid);
    }

    private void AddBoundSlider(string label, BlockViewModel block, string path, double min, double max)
    {
        AddLabel(label);
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = 1, IsSnapToTickEnabled = true };
        slider.SetBinding(RangeBase.ValueProperty, new Binding(path) { Source = block, Mode = BindingMode.TwoWay });
        var valueLabel = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 11,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 0, 12)
        };
        valueLabel.SetBinding(TextBlock.TextProperty, new Binding(path) { Source = block });
        PropertiesContainer.Children.Add(slider);
        PropertiesContainer.Children.Add(valueLabel);
    }

    private void AddBoundCombo(string label, BlockViewModel block, string path, string[] options)
    {
        AddLabel(label);
        var current = typeof(BlockViewModel).GetProperty(path)!.GetValue(block) as string ?? options[0];
        var items = options.Contains(current) ? options : options.Prepend(current).ToArray();
        var comboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 12), ItemsSource = items, ItemTemplate = ValueTemplate };
        comboBox.SetBinding(Selector.SelectedItemProperty, new Binding(path) { Source = block, Mode = BindingMode.TwoWay });
        PropertiesContainer.Children.Add(comboBox);
    }

    private void AddNumberPair(BlockViewModel block, (string Label, string Path) first, (string Label, string Path) second)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var column = 0;
        foreach (var (label, path) in new[] { first, second })
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var box = new TextBox();
            box.SetBinding(TextBox.TextProperty, new Binding(path)
            {
                Source = block,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                StringFormat = "0",
                ConverterCulture = CultureInfo.InvariantCulture
            });
            panel.Children.Add(box);
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
            column += 2;
        }
        PropertiesContainer.Children.Add(grid);
    }

    // ========== Validation and actions ==========

    private void AddActionButtons(BlockViewModel block)
    {
        var validate = new Button { Content = Loc.T("Validate block"), Margin = new Thickness(0, 16, 0, 8), Padding = new Thickness(12, 6, 12, 6) };
        validate.Click += (_, _) =>
        {
            var (isValid, errors) = BlockValidator.ValidateBlock(block.Model);
            if (isValid)
                ThemedMessageBox.Show(Loc.T("✓ Block is valid!"), Loc.T("Validation"), MessageBoxButton.OK, MessageBoxImage.Information);
            else
                ThemedMessageBox.Show(Loc.F("Problems found:\n\n• {0}", string.Join("\n• ", errors)), Loc.T("Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        PropertiesContainer.Children.Add(validate);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var reset = new Button { Content = Loc.T("Reset style"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 6, 10, 6) };
        reset.Click += (_, _) =>
        {
            block.ResetStyle();
            UpdatePropertiesPanel(block);
        };
        var duplicate = new Button { Content = Loc.T("Duplicate"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 6, 10, 6) };
        duplicate.Click += (_, _) => _viewModel?.DuplicateBlock(block);
        var delete = new Button { Content = Loc.T("Delete"), Padding = new Thickness(10, 6, 10, 6), Background = ErrorBrush };
        delete.Click += (_, _) =>
        {
            if (_viewModel != null &&
                ThemedMessageBox.Show(Loc.F("Delete {0} block?", Loc.Block(block.Type)), Loc.T("Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _viewModel.RemoveBlock(block);
            }
        };
        actions.Children.Add(reset);
        actions.Children.Add(duplicate);
        actions.Children.Add(delete);
        PropertiesContainer.Children.Add(actions);

        var order = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var up = new Button { Content = Loc.T("▲ Move up"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 6, 10, 6), ToolTip = Loc.T("Earlier on the page") };
        up.Click += (_, _) => _viewModel?.MoveInOrder(block, -1);
        var down = new Button { Content = Loc.T("▼ Move down"), Padding = new Thickness(10, 6, 10, 6), ToolTip = Loc.T("Later on the page") };
        down.Click += (_, _) => _viewModel?.MoveInOrder(block, +1);
        order.Children.Add(up);
        order.Children.Add(down);
        PropertiesContainer.Children.Add(order);
    }

    // ========== Helpers ==========

    private void AddSection(string text)
    {
        PropertiesContainer.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 8),
            Foreground = AccentBrush,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void AddLabel(string text)
    {
        PropertiesContainer.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap
        });
    }

    private static TextBox CreateTextBox(bool multiline) => multiline
        ? new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 64,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12)
        }
        : new TextBox { Margin = new Thickness(0, 0, 0, 12) };

    private static string FormatNumber(double value, double step) =>
        step < 1 ? value.ToString("0.0#", CultureInfo.InvariantCulture) : value.ToString("0", CultureInfo.InvariantCulture);
}
