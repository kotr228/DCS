namespace GrayCat.Core.Generation;

/// <summary>
/// Default properties for every block type in the editor palette.
/// The editor copies these into new blocks; the generator fills in anything a block is missing.
/// </summary>
public static class BlockDefaults
{
    public static readonly IReadOnlyList<string> BlockTypes = new[]
    {
        "Text", "Header", "Navbar", "Hero", "Button", "Card", "Image", "Gallery", "ProductGrid",
        "Form", "ContactForm", "Video", "Testimonial", "Pricing", "Accordion", "Stats",
        "SocialLinks", "Divider", "Footer"
    };

    private static Dictionary<string, object?> Item(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static List<object?> List(params object?[] items) => items.ToList();

    /// <summary>Returns a fresh copy of the defaults (safe to modify).</summary>
    public static Dictionary<string, object?> For(string blockType) => blockType switch
    {
        "Text" => new(),
        "Header" => new()
        {
            ["title"] = "Welcome",
            ["subtitle"] = "",
            ["showNavigation"] = true,
            ["pagesInMenu"] = true,
            ["links"] = List(
                Item(("text", "Home"), ("url", "#home")),
                Item(("text", "About"), ("url", "#about")),
                Item(("text", "Contact"), ("url", "#contact")))
        },
        "Navbar" => new()
        {
            ["brandName"] = "Brand",
            ["logo"] = "",
            ["pagesInMenu"] = true,
            ["position"] = "static",
            ["transparent"] = false,
            ["showSearch"] = false,
            ["ctaText"] = "",
            ["ctaUrl"] = "#",
            ["menuItems"] = List(
                Item(("text", "Home"), ("url", "#")),
                Item(("text", "About"), ("url", "#about")),
                Item(("text", "Contact"), ("url", "#contact")))
        },
        "Hero" => new()
        {
            ["headline"] = "Welcome to Our Site",
            ["subheadline"] = "Build amazing things together",
            ["ctaText"] = "Get Started",
            ["ctaUrl"] = "#",
            ["backgroundImage"] = "",
            ["overlay"] = true,
            ["overlayOpacity"] = 0.5,
            ["alignment"] = "center",
            ["height"] = "medium"
        },
        "Button" => new()
        {
            ["text"] = "Click Me",
            ["url"] = "#",
            ["variant"] = "primary",
            ["size"] = "medium",
            ["fullWidth"] = false,
            ["disabled"] = false,
            ["target"] = "_self"
        },
        "Card" => new()
        {
            ["title"] = "Card Title",
            ["description"] = "Card description text goes here",
            ["image"] = "",
            ["imagePosition"] = "top",
            ["linkText"] = "Learn More",
            ["linkUrl"] = "#",
            ["shadow"] = true,
            ["bordered"] = false,
            ["hoverable"] = true
        },
        "Image" => new()
        {
            ["src"] = "",
            ["alt"] = "Image",
            ["caption"] = ""
        },
        "Gallery" => new()
        {
            ["title"] = "Gallery",
            ["columns"] = 3.0,
            ["images"] = List(
                Item(("src", "https://picsum.photos/seed/graycat1/600/400"), ("alt", "Photo 1"), ("caption", "")),
                Item(("src", "https://picsum.photos/seed/graycat2/600/400"), ("alt", "Photo 2"), ("caption", "")),
                Item(("src", "https://picsum.photos/seed/graycat3/600/400"), ("alt", "Photo 3"), ("caption", "")))
        },
        "ProductGrid" => new()
        {
            ["title"] = "Our Products",
            ["currency"] = "$",
            ["columns"] = 3.0,
            ["buttonText"] = "View",
            ["products"] = List(
                Item(("name", "Product One"), ("price", "19"), ("description", "Short product description"), ("image", "https://picsum.photos/seed/gcprod1/400/300"), ("url", "#")),
                Item(("name", "Product Two"), ("price", "29"), ("description", "Short product description"), ("image", "https://picsum.photos/seed/gcprod2/400/300"), ("url", "#")),
                Item(("name", "Product Three"), ("price", "39"), ("description", "Short product description"), ("image", "https://picsum.photos/seed/gcprod3/400/300"), ("url", "#")))
        },
        "Form" => new()
        {
            ["title"] = "Subscribe",
            ["subtitle"] = "Get the latest news straight to your inbox",
            ["submitText"] = "Subscribe",
            ["submitUrl"] = "/api/subscribe",
            ["successMessage"] = "Thanks for subscribing!",
            ["errorMessage"] = "Oops! Something went wrong. Please try again.",
            ["fields"] = List(
                Item(("name", "email"), ("type", "email"), ("label", "Email"), ("required", true)))
        },
        "ContactForm" => new()
        {
            ["title"] = "Get in Touch",
            ["subtitle"] = "We'd love to hear from you",
            ["submitText"] = "Send Message",
            ["submitUrl"] = "/api/contact",
            ["successMessage"] = "Thank you! We'll get back to you soon.",
            ["errorMessage"] = "Oops! Something went wrong. Please try again.",
            ["showPhoneField"] = true,
            ["showSubjectField"] = false,
            ["fields"] = List(
                Item(("name", "name"), ("type", "text"), ("label", "Name"), ("required", true)),
                Item(("name", "email"), ("type", "email"), ("label", "Email"), ("required", true)),
                Item(("name", "message"), ("type", "textarea"), ("label", "Message"), ("required", true)))
        },
        "Video" => new()
        {
            ["videoUrl"] = "",
            ["provider"] = "youtube",
            ["autoplay"] = false,
            ["controls"] = true,
            ["loop"] = false,
            ["muted"] = false,
            ["aspectRatio"] = "16:9"
        },
        "Testimonial" => new()
        {
            ["quote"] = "This product changed my life!",
            ["author"] = "John Doe",
            ["role"] = "CEO, Company",
            ["avatar"] = "",
            ["rating"] = 5.0,
            ["showRating"] = true,
            ["layout"] = "default"
        },
        "Pricing" => new()
        {
            ["planName"] = "Pro Plan",
            ["price"] = "29",
            ["currency"] = "$",
            ["period"] = "month",
            ["features"] = List("Feature 1", "Feature 2", "Feature 3"),
            ["ctaText"] = "Get Started",
            ["ctaUrl"] = "#",
            ["highlighted"] = false,
            ["badge"] = ""
        },
        "Accordion" => new()
        {
            ["title"] = "Frequently Asked Questions",
            ["allowMultiple"] = false,
            ["defaultOpen"] = 0.0,
            ["items"] = List(
                Item(("question", "What is this?"), ("answer", "This is an answer.")),
                Item(("question", "How does it work?"), ("answer", "It works like this.")))
        },
        "Stats" => new()
        {
            ["layout"] = "horizontal",
            ["animated"] = true,
            ["stats"] = List(
                Item(("number", "1000+"), ("label", "Happy Clients")),
                Item(("number", "50+"), ("label", "Projects Completed")),
                Item(("number", "24/7"), ("label", "Support")))
        },
        "SocialLinks" => new()
        {
            ["style"] = "icons",
            ["size"] = "medium",
            ["alignment"] = "center",
            ["links"] = List(
                Item(("platform", "facebook"), ("url", "https://facebook.com")),
                Item(("platform", "twitter"), ("url", "https://twitter.com")),
                Item(("platform", "instagram"), ("url", "https://instagram.com")))
        },
        "Divider" => new()
        {
            ["style"] = "solid",
            ["thickness"] = 1.0,
            ["color"] = "#DDDDDD",
            ["spacing"] = "medium",
            ["withIcon"] = false,
            ["icon"] = "★"
        },
        "Footer" => new()
        {
            ["copyright"] = "",
            ["links"] = List(
                Item(("text", "Privacy"), ("url", "#privacy")),
                Item(("text", "Terms"), ("url", "#terms")),
                Item(("text", "Contact"), ("url", "#contact")))
        },
        _ => new()
    };

    /// <summary>Default text shown in a block's Content field when it is created.</summary>
    public static string DefaultContent(string blockType, string? language = null) =>
        blockType == "Text" ? ContentText.T(language, "Write your text here.") : string.Empty;

    /// <summary>Defaults with their texts in the site's language (used when a block is created).</summary>
    public static Dictionary<string, object?> For(string blockType, string? language) =>
        For(blockType).ToDictionary(kv => kv.Key, kv => ContentText.Localize(language, kv.Value));
}
