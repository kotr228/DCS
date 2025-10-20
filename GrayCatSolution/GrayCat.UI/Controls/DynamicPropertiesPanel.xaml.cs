namespace GrayCat.UI.Controls;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using GrayCat.Core.Models;
using GrayCat.Core.Validation;
using GrayCat.UI.ViewModels;

public partial class DynamicPropertiesPanel : UserControl
{
    private BlockViewModel? _currentBlock;

    public DynamicPropertiesPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedBlock))
                {
                    UpdatePropertiesPanel(viewModel.SelectedBlock);
                }
            };
        }
    }

    private void UpdatePropertiesPanel(BlockViewModel? block)
    {
        _currentBlock = block;
        PropertiesContainer.Children.Clear();

        if (block == null)
        {
            var noSelection = new TextBlock
            {
                Text = "No block selected",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
                Margin = new Thickness(0, 20, 0, 0),
                TextAlignment = TextAlignment.Center
            };
            PropertiesContainer.Children.Add(noSelection);
            return;
        }

        // Header
        var header = new TextBlock
        {
            Text = $"{block.Type} Properties",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 20)
        };
        PropertiesContainer.Children.Add(header);

        // Common properties
        AddCommonProperties(block);

        // Type-specific properties
        AddTypeSpecificProperties(block);

        // Validation button
        AddValidationButton(block);

        // Actions
        AddActionButtons(block);
    }

    private void AddCommonProperties(BlockViewModel block)
    {
        var commonSection = CreateSectionHeader("Common Properties");
        PropertiesContainer.Children.Add(commonSection);

        // Content/Text
        AddTextProperty("Content", block, nameof(block.Content), multiline: true);

        // Font Size
        AddSliderProperty("Font Size", block, nameof(block.FontSize), 8, 72, "px");

        // Text Color
        AddColorProperty("Text Color", block, nameof(block.Color));

        // Background
        AddColorProperty("Background", block, nameof(block.Background));

        // Text Alignment
        AddComboBoxProperty("Text Alignment", block, nameof(block.TextAlignment),
            new[] { "Left", "Center", "Right", "Justify" });

        // Position
        AddPositionProperty(block);

        // Size
        AddSizeProperty(block);
    }

    private void AddTypeSpecificProperties(BlockViewModel block)
    {
        var specificSection = CreateSectionHeader($"{block.Type}-Specific Properties");
        PropertiesContainer.Children.Add(specificSection);

        switch (block.Type)
        {
            case "Button":
                AddButtonProperties(block);
                break;
            case "ContactForm":
                AddContactFormProperties(block);
                break;
            case "Hero":
                AddHeroProperties(block);
                break;
            case "Card":
                AddCardProperties(block);
                break;
            case "Navbar":
                AddNavbarProperties(block);
                break;
            case "Video":
                AddVideoProperties(block);
                break;
            case "Testimonial":
                AddTestimonialProperties(block);
                break;
            case "Pricing":
                AddPricingProperties(block);
                break;
            case "Accordion":
                AddAccordionProperties(block);
                break;
            case "Stats":
                AddStatsProperties(block);
                break;
            case "SocialLinks":
                AddSocialLinksProperties(block);
                break;
            case "Divider":
                AddDividerProperties(block);
                break;
        }
    }

    // ========== TYPE-SPECIFIC PROPERTY BUILDERS ==========

    private void AddButtonProperties(BlockViewModel block)
    {
        AddPropertyField("Button Text", "text", block);
        AddPropertyField("URL", "url", block);
        AddPropertyComboBox("Variant", "variant", block,
            new[] { "primary", "secondary", "success", "danger", "warning", "info" });
        AddPropertyComboBox("Size", "size", block,
            new[] { "small", "medium", "large" });
        AddPropertyCheckBox("Full Width", "fullWidth", block);
        AddPropertyCheckBox("Disabled", "disabled", block);
    }

    private void AddContactFormProperties(BlockViewModel block)
    {
        AddPropertyField("Form Title", "title", block);
        AddPropertyField("Subtitle", "subtitle", block);
        AddPropertyField("Submit URL", "submitUrl", block);
        AddPropertyField("Submit Button Text", "submitText", block);
        AddPropertyField("Success Message", "successMessage", block, multiline: true);
        AddPropertyField("Error Message", "errorMessage", block, multiline: true);
        AddPropertyCheckBox("Show Phone Field", "showPhoneField", block);
        AddPropertyCheckBox("Show Subject Field", "showSubjectField", block);
    }

    private void AddHeroProperties(BlockViewModel block)
    {
        AddPropertyField("Headline", "headline", block);
        AddPropertyField("Subheadline", "subheadline", block, multiline: true);
        AddPropertyField("CTA Text", "ctaText", block);
        AddPropertyField("CTA URL", "ctaUrl", block);
        AddPropertyField("Background Image", "backgroundImage", block);
        AddPropertyCheckBox("Show Overlay", "overlay", block);
        AddPropertySlider("Overlay Opacity", "overlayOpacity", block, 0, 1, 0.1);
        AddPropertyComboBox("Alignment", "alignment", block,
            new[] { "left", "center", "right" });
        AddPropertyComboBox("Height", "height", block,
            new[] { "small", "medium", "large", "full" });
    }

    private void AddCardProperties(BlockViewModel block)
    {
        AddPropertyField("Card Title", "title", block);
        AddPropertyField("Description", "description", block, multiline: true);
        AddPropertyField("Image URL", "image", block);
        AddPropertyField("Link Text", "linkText", block);
        AddPropertyField("Link URL", "linkUrl", block);
        AddPropertyComboBox("Image Position", "imagePosition", block,
            new[] { "top", "left", "right", "bottom" });
        AddPropertyCheckBox("Show Shadow", "shadow", block);
        AddPropertyCheckBox("Bordered", "bordered", block);
        AddPropertyCheckBox("Hoverable", "hoverable", block);
    }

    private void AddNavbarProperties(BlockViewModel block)
    {
        AddPropertyField("Brand Name", "brandName", block);
        AddPropertyField("Logo URL", "logo", block);
        AddPropertyComboBox("Position", "position", block,
            new[] { "static", "fixed", "sticky" });
        AddPropertyCheckBox("Transparent", "transparent", block);
        AddPropertyCheckBox("Show Search", "showSearch", block);
        AddPropertyField("CTA Text", "ctaText", block);
        AddPropertyField("CTA URL", "ctaUrl", block);

        var menuButton = new Button
        {
            Content = "Edit Menu Items",
            Margin = new Thickness(0, 5, 0, 15)
        };
        menuButton.Click += (s, e) => EditJsonProperty(block, "menuItems", "Menu Items");
        PropertiesContainer.Children.Add(menuButton);
    }

    private void AddVideoProperties(BlockViewModel block)
    {
        AddPropertyField("Video URL", "videoUrl", block);
        AddPropertyComboBox("Provider", "provider", block,
            new[] { "youtube", "vimeo", "custom" });
        AddPropertyComboBox("Aspect Ratio", "aspectRatio", block,
            new[] { "16:9", "4:3", "1:1" });
        AddPropertyCheckBox("Autoplay", "autoplay", block);
        AddPropertyCheckBox("Show Controls", "controls", block);
        AddPropertyCheckBox("Loop", "loop", block);
        AddPropertyCheckBox("Muted", "muted", block);
    }

    private void AddTestimonialProperties(BlockViewModel block)
    {
        AddPropertyField("Quote", "quote", block, multiline: true);
        AddPropertyField("Author Name", "author", block);
        AddPropertyField("Author Role", "role", block);
        AddPropertyField("Avatar URL", "avatar", block);
        AddPropertySlider("Rating", "rating", block, 0, 5, 1);
        AddPropertyCheckBox("Show Rating", "showRating", block);
        AddPropertyComboBox("Layout", "layout", block,
            new[] { "default", "card", "minimal" });
    }

    private void AddPricingProperties(BlockViewModel block)
    {
        AddPropertyField("Plan Name", "planName", block);
        AddPropertyField("Price", "price", block);
        AddPropertyField("Currency", "currency", block);
        AddPropertyField("Period", "period", block);
        AddPropertyField("CTA Text", "ctaText", block);
        AddPropertyField("CTA URL", "ctaUrl", block);
        AddPropertyField("Badge", "badge", block);
        AddPropertyCheckBox("Highlighted", "highlighted", block);

        var featuresButton = new Button
        {
            Content = "Edit Features",
            Margin = new Thickness(0, 5, 0, 15)
        };
        featuresButton.Click += (s, e) => EditJsonProperty(block, "features", "Features List");
        PropertiesContainer.Children.Add(featuresButton);
    }

    private void AddAccordionProperties(BlockViewModel block)
    {
        AddPropertyField("Section Title", "title", block);
        AddPropertyCheckBox("Allow Multiple Open", "allowMultiple", block);
        AddPropertyField("Default Open Index", "defaultOpen", block);

        var itemsButton = new Button
        {
            Content = "Edit FAQ Items",
            Margin = new Thickness(0, 5, 0, 15)
        };
        itemsButton.Click += (s, e) => EditJsonProperty(block, "items", "Accordion Items");
        PropertiesContainer.Children.Add(itemsButton);
    }

    private void AddStatsProperties(BlockViewModel block)
    {
        AddPropertyComboBox("Layout", "layout", block,
            new[] { "horizontal", "vertical", "grid" });
        AddPropertyCheckBox("Animated", "animated", block);

        var statsButton = new Button
        {
            Content = "Edit Statistics",
            Margin = new Thickness(0, 5, 0, 15)
        };
        statsButton.Click += (s, e) => EditJsonProperty(block, "stats", "Statistics");
        PropertiesContainer.Children.Add(statsButton);
    }

    private void AddSocialLinksProperties(BlockViewModel block)
    {
        AddPropertyComboBox("Style", "style", block,
            new[] { "icons", "buttons", "text" });
        AddPropertyComboBox("Size", "size", block,
            new[] { "small", "medium", "large" });
        AddPropertyComboBox("Alignment", "alignment", block,
            new[] { "left", "center", "right" });

        var linksButton = new Button
        {
            Content = "Edit Social Links",
            Margin = new Thickness(0, 5, 0, 15)
        };
        linksButton.Click += (s, e) => EditJsonProperty(block, "links", "Social Links");
        PropertiesContainer.Children.Add(linksButton);
    }

    private void AddDividerProperties(BlockViewModel block)
    {
        AddPropertyComboBox("Style", "style", block,
            new[] { "solid", "dashed", "dotted", "double" });
        AddPropertySlider("Thickness", "thickness", block, 1, 10, 1);
        AddPropertyField("Color", "color", block);
        AddPropertyComboBox("Spacing", "spacing", block,
            new[] { "small", "medium", "large" });
        AddPropertyCheckBox("With Icon", "withIcon", block);
        AddPropertyField("Icon", "icon", block);
    }

    // ========== HELPER METHODS ==========

    private TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 15, 0, 10),
            Foreground = new SolidColorBrush(Color.FromRgb(0, 102, 204))
        };
    }

    private void AddPropertyField(string label, string propertyKey, BlockViewModel block, bool multiline = false)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        PropertiesContainer.Children.Add(labelBlock);

        var textBox = multiline ? new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 60,
            Margin = new Thickness(0, 0, 0, 15)
        } : new TextBox
        {
            Margin = new Thickness(0, 0, 0, 15)
        };

        if (block.Properties.TryGetValue(propertyKey, out var value))
        {
            textBox.Text = value?.ToString() ?? "";
        }

        textBox.TextChanged += (s, e) =>
        {
            block.Properties[propertyKey] = textBox.Text;
        };

        PropertiesContainer.Children.Add(textBox);
    }

    private void AddPropertyComboBox(string label, string propertyKey, BlockViewModel block, string[] options)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        PropertiesContainer.Children.Add(labelBlock);

        var comboBox = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 15)
        };

        foreach (var option in options)
        {
            comboBox.Items.Add(option);
        }

        if (block.Properties.TryGetValue(propertyKey, out var value))
        {
            comboBox.SelectedItem = value?.ToString();
        }
        else if (options.Length > 0)
        {
            comboBox.SelectedIndex = 0;
        }

        comboBox.SelectionChanged += (s, e) =>
        {
            if (comboBox.SelectedItem != null)
            {
                block.Properties[propertyKey] = comboBox.SelectedItem.ToString();
            }
        };

        PropertiesContainer.Children.Add(comboBox);
    }

    private void AddPropertyCheckBox(string label, string propertyKey, BlockViewModel block)
    {
        var checkBox = new CheckBox
        {
            Content = label,
            Margin = new Thickness(0, 0, 0, 10)
        };

        if (block.Properties.TryGetValue(propertyKey, out var value))
        {
            checkBox.IsChecked = value is bool b && b;
        }

        checkBox.Checked += (s, e) => block.Properties[propertyKey] = true;
        checkBox.Unchecked += (s, e) => block.Properties[propertyKey] = false;

        PropertiesContainer.Children.Add(checkBox);
    }

    private void AddPropertySlider(string label, string propertyKey, BlockViewModel block, double min, double max, double step)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        PropertiesContainer.Children.Add(labelBlock);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 0, 0, 5)
        };

        if (block.Properties.TryGetValue(propertyKey, out var value))
        {
            if (double.TryParse(value?.ToString(), out var doubleValue))
            {
                slider.Value = doubleValue;
            }
        }

        var valueLabel = new TextBlock
        {
            Text = slider.Value.ToString("F2"),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
            Margin = new Thickness(0, 0, 0, 15)
        };

        slider.ValueChanged += (s, e) =>
        {
            block.Properties[propertyKey] = slider.Value;
            valueLabel.Text = slider.Value.ToString("F2");
        };

        PropertiesContainer.Children.Add(slider);
        PropertiesContainer.Children.Add(valueLabel);
    }

    private void AddValidationButton(BlockViewModel block)
    {
        var validateButton = new Button
        {
            Content = "Validate Block",
            Margin = new Thickness(0, 20, 0, 10),
            Padding = new Thickness(15, 8, 15, 8)
        };

        validateButton.Click += (s, e) =>
        {
            var (isValid, errors) = BlockValidator.ValidateBlock(block.Model);

            if (isValid)
            {
                MessageBox.Show("✓ Block is valid!", "Validation Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var errorMessage = string.Join("\n• ", errors);
                MessageBox.Show($"Validation errors:\n• {errorMessage}", "Validation Failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        PropertiesContainer.Children.Add(validateButton);
    }

    private void AddActionButtons(BlockViewModel block)
    {
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var resetButton = new Button
        {
            Content = "Reset",
            Width = 80,
            Margin = new Thickness(0, 0, 10, 0)
        };
        resetButton.Click += (s, e) => ResetBlock(block);

        var deleteButton = new Button
        {
            Content = "Delete",
            Width = 80
        };
        deleteButton.Click += (s, e) => DeleteBlock(block);

        actionsPanel.Children.Add(resetButton);
        actionsPanel.Children.Add(deleteButton);

        PropertiesContainer.Children.Add(actionsPanel);
    }

    // Additional helper methods...
    private void EditJsonProperty(BlockViewModel block, string propertyKey, string title)
    {
        // TODO: Implement JSON editor dialog
        MessageBox.Show($"JSON editor for {title} coming soon!", "Feature",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ResetBlock(BlockViewModel block)
    {
        // Reset to defaults
        block.Color = "#000000";
        block.Background = "#FFFFFF";
        block.FontSize = 14;
        UpdatePropertiesPanel(block);
    }

    private void DeleteBlock(BlockViewModel block)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var result = MessageBox.Show($"Delete {block.Type} block?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                viewModel.RemoveBlock(block);
            }
        }
    }

    // === Compatibility wrappers for internal AddProperty* calls ===
    private void AddTextProperty(string label, BlockViewModel block, string binding, bool multiline = false)
    {
        AddPropertyField(label, binding, block, multiline);
    }

    private void AddSliderProperty(string label, BlockViewModel block, string binding, double min, double max, string? unit = null)
    {
        // TickFrequency = 1 (default step)
        AddPropertySlider(label, binding, block, min, max, 1);
    }

    private void AddColorProperty(string label, BlockViewModel block, string binding)
    {
        AddPropertyField(label, binding, block);
    }

    private void AddComboBoxProperty(string label, BlockViewModel block, string binding, string[] options)
    {
        AddPropertyComboBox(label, binding, block, options);
    }

    private void AddPositionProperty(BlockViewModel block)
    {
        AddPropertyField("X Position", nameof(block.PositionX), block);
        AddPropertyField("Y Position", nameof(block.PositionY), block);
    }

    private void AddSizeProperty(BlockViewModel block)
    {
        AddPropertyField("Width", nameof(block.Width), block);
        AddPropertyField("Height", nameof(block.Height), block);
    }

}