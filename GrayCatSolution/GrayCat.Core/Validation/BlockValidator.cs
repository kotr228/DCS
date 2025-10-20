namespace GrayCat.Core.Validation;

using GrayCat.Core.Models;
using System.Text.RegularExpressions;

public class BlockValidator
{
    private static readonly Regex EmailRegex = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"^(https?:\/\/)?([\da-z\.-]+)\.([a-z\.]{2,6})([\/\w \.-]*)*\/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HexColorRegex = new(@"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$", RegexOptions.Compiled);

    public static (bool IsValid, List<string> Errors) ValidateBlock(BlockModel block)
    {
        var errors = new List<string>();

        // Common validations
        if (string.IsNullOrWhiteSpace(block.Type))
        {
            errors.Add("Block type is required");
        }

        if (block.Width < 50 || block.Width > 2000)
        {
            errors.Add("Width must be between 50 and 2000 pixels");
        }

        if (block.Height < 30 || block.Height > 2000)
        {
            errors.Add("Height must be between 30 and 2000 pixels");
        }

        if (!IsValidHexColor(block.Color))
        {
            errors.Add($"Invalid color format: {block.Color}");
        }

        if (!IsValidHexColor(block.Background))
        {
            errors.Add($"Invalid background color format: {block.Background}");
        }

        if (block.FontSize < 8 || block.FontSize > 72)
        {
            errors.Add("Font size must be between 8 and 72");
        }

        // Type-specific validations
        switch (block.Type)
        {
            case "Button":
                errors.AddRange(ValidateButtonBlock(block));
                break;
            case "ContactForm":
                errors.AddRange(ValidateContactFormBlock(block));
                break;
            case "Navbar":
                errors.AddRange(ValidateNavbarBlock(block));
                break;
            case "Card":
                errors.AddRange(ValidateCardBlock(block));
                break;
            case "Hero":
                errors.AddRange(ValidateHeroBlock(block));
                break;
            case "Image":
                errors.AddRange(ValidateImageBlock(block));
                break;
        }

        return (!errors.Any(), errors);
    }

    private static List<string> ValidateButtonBlock(BlockModel block)
    {
        var errors = new List<string>();

        if (block.Properties.TryGetValue("text", out var text) &&
            string.IsNullOrWhiteSpace(text?.ToString()))
        {
            errors.Add("Button text cannot be empty");
        }

        if (block.Properties.TryGetValue("url", out var url) &&
            !string.IsNullOrEmpty(url?.ToString()) &&
            url.ToString() != "#" &&
            !IsValidUrl(url.ToString()))
        {
            errors.Add("Button URL is invalid");
        }

        if (block.Properties.TryGetValue("variant", out var variant))
        {
            var validVariants = new[] { "primary", "secondary", "success", "danger", "warning", "info" };
            if (!validVariants.Contains(variant?.ToString()?.ToLower()))
            {
                errors.Add("Invalid button variant");
            }
        }

        return errors;
    }

    private static List<string> ValidateContactFormBlock(BlockModel block)
    {
        var errors = new List<string>();

        if (block.Properties.TryGetValue("title", out var title) &&
            string.IsNullOrWhiteSpace(title?.ToString()))
        {
            errors.Add("Form title cannot be empty");
        }

        if (block.Properties.TryGetValue("submitUrl", out var submitUrl) &&
            string.IsNullOrWhiteSpace(submitUrl?.ToString()))
        {
            errors.Add("Submit URL is required for contact form");
        }

        if (block.Properties.TryGetValue("fields", out var fields))
        {
            try
            {
                var fieldsStr = fields?.ToString();
                if (string.IsNullOrWhiteSpace(fieldsStr))
                {
                    errors.Add("Form must have at least one field");
                }
                else
                {
                    var fieldsList = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(fieldsStr);
                    if (fieldsList == null || !fieldsList.Any())
                    {
                        errors.Add("Form must have at least one field");
                    }
                    else
                    {
                        foreach (var field in fieldsList)
                        {
                            if (!field.ContainsKey("name") || string.IsNullOrEmpty(field["name"]?.ToString()))
                            {
                                errors.Add("Each form field must have a name");
                            }
                            if (!field.ContainsKey("type") || string.IsNullOrEmpty(field["type"]?.ToString()))
                            {
                                errors.Add("Each form field must have a type");
                            }
                        }
                    }
                }
            }
            catch
            {
                errors.Add("Form fields JSON is invalid");
            }
        }

        return errors;
    }

    private static List<string> ValidateNavbarBlock(BlockModel block)
    {
        var errors = new List<string>();

        if (block.Properties.TryGetValue("brandName", out var brandName) &&
            string.IsNullOrWhiteSpace(brandName?.ToString()))
        {
            errors.Add("Brand name cannot be empty");
        }

        if (block.Properties.TryGetValue("menuItems", out var menuItems))
        {
            try
            {
                var itemsStr = menuItems?.ToString();
                if (!string.IsNullOrWhiteSpace(itemsStr))
                {
                    var itemsList = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(itemsStr);
                    if (itemsList != null)
                    {
                        foreach (var item in itemsList)
                        {
                            if (!item.ContainsKey("text") || string.IsNullOrEmpty(item["text"]))
                            {
                                errors.Add("Each menu item must have text");
                            }
                        }
                    }
                }
            }
            catch
            {
                errors.Add("Menu items JSON is invalid");
            }
        }

        return errors;
    }

    private static List<string> ValidateCardBlock(BlockModel block)
    {
        var errors = new List<string>();

        if (block.Properties.TryGetValue("title", out var title) &&
            string.IsNullOrWhiteSpace(title?.ToString()))
        {
            errors.Add("Card title cannot be empty");
        }

        if (block.Properties.TryGetValue("image", out var image) &&
            !string.IsNullOrEmpty(image?.ToString()) &&
            !IsValidUrl(image.ToString()) &&
            !IsValidImagePath(image.ToString()))
        {
            errors.Add("Card image path or URL is invalid");
        }

        return errors;
    }

    private static List<string> ValidateHeroBlock(BlockModel block)
    {
        var errors = new List<string>();

        if (block.Properties.TryGetValue("headline", out var headline) &&
            string.IsNullOrWhiteSpace(headline?.ToString()))
        {
            errors.Add("Hero headline cannot be empty");
        }

        if (block.Properties.TryGetValue("backgroundImage", out var bgImage) &&
            !string.IsNullOrEmpty(bgImage?.ToString()) &&
            !IsValidUrl(bgImage.ToString()) &&
            !IsValidImagePath(bgImage.ToString()))
        {
            errors.Add("Background image path or URL is invalid");
        }

        if (block.Properties.TryGetValue("overlayOpacity", out var opacity))
        {
            if (double.TryParse(opacity?.ToString(), out var opacityValue))
            {
                if (opacityValue < 0 || opacityValue > 1)
                {
                    errors.Add("Overlay opacity must be between 0 and 1");
                }
            }
        }

        return errors;
    }

    private static List<string> ValidateImageBlock(BlockModel block)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(block.ImagePath))
        {
            errors.Add("Image path is required");
        }
        else if (!IsValidUrl(block.ImagePath) && !IsValidImagePath(block.ImagePath))
        {
            errors.Add("Image path or URL is invalid");
        }

        if (block.Properties.TryGetValue("alt", out var alt) &&
            string.IsNullOrWhiteSpace(alt?.ToString()))
        {
            errors.Add("Image alt text is required for accessibility");
        }

        return errors;
    }

    // Helper validation methods
    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        return EmailRegex.IsMatch(email);
    }

    public static bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        return UrlRegex.IsMatch(url) || url.StartsWith("/") || url == "#";
    }

    public static bool IsValidHexColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return false;
        return HexColorRegex.IsMatch(color);
    }

    public static bool IsValidImagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".svg", ".webp" };
        return validExtensions.Any(ext => path.ToLower().EndsWith(ext));
    }

    public static bool IsValidJsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            System.Text.Json.JsonSerializer.Deserialize<List<object>>(json);
            return true;
        }
        catch
        {
            return false;
        }
    }
}