namespace GrayCat.Core.Generation;

using System.Text.Json;
using GrayCat.Core.Models;

/// <summary>Starter content for new projects.</summary>
public static class ProjectTemplates
{
    public const string Blank = "Blank page";
    public const string Landing = "Landing page";
    public const string Business = "Business site (4 pages)";
    public const string Portfolio = "Portfolio";
    public const string Shop = "Online shop";

    public static readonly IReadOnlyList<string> Names = new[] { Landing, Business, Portfolio, Shop, Blank };

    public static string Describe(string name) => name switch
    {
        Blank => "An empty page to build from scratch.",
        Landing => "One page: navigation, hero, statistics, pricing, contact form and footer.",
        Business => "Home, About, Services and Contact pages with a shared menu.",
        Portfolio => "Hero, gallery, testimonials and a contact form.",
        Shop => "Product grid, pricing, FAQ and a newsletter form.",
        _ => string.Empty
    };

    /// <summary>Fills <paramref name="project"/> with the template's pages and blocks.</summary>
    public static void Apply(ProjectModel project, string template, string? language = null)
    {
        string L(string english) => ContentText.T(language, english);
        string LF(string english, object arg) => ContentText.F(language, english, arg);
        var name = string.IsNullOrWhiteSpace(project.ProjectName) ? "My Site" : project.ProjectName.Trim();
        project.Pages = new List<PageInfo> { new() { Slug = "", Title = L("Home") } };
        project.Blocks = new List<BlockModel>();
        var builder = new Builder(project, language);

        switch (template)
        {
            case Landing:
                builder.Page("")
                    .Add("Navbar", ("brandName", name), ("ctaText", "Contact us"), ("ctaUrl", "#contact"),
                        ("menuItems", Links(("Features", "#features"), ("Pricing", "#pricing"), ("Contact", "#contact"))))
                    .Add("Hero", ("headline", LF("Welcome to {0}", name)), ("subheadline", "Everything you need, in one place."), ("ctaText", "See pricing"), ("ctaUrl", "#pricing"))
                    .Add("Stats", ("anchor", "features"))
                    .Add("Pricing", ("highlighted", true), ("badge", "Most popular"))
                    .Add("Testimonial")
                    .Add("ContactForm")
                    .Add("Footer");
                break;

            case Business:
                project.Pages.Add(new PageInfo { Slug = "about", Title = L("About") });
                project.Pages.Add(new PageInfo { Slug = "services", Title = L("Services") });
                project.Pages.Add(new PageInfo { Slug = "contact", Title = L("Contact") });
                var nav = new (string, object)[] { ("brandName", name), ("menuItems", Links()) };
                var footer = new (string, object)[] { ("links", Links(("About", "/about"), ("Services", "/services"), ("Contact", "/contact"))) };
                builder.Page("")
                    .Add("Navbar", nav)
                    .Add("Hero", ("headline", name), ("subheadline", "Reliable services for growing businesses."), ("ctaText", "Our services"), ("ctaUrl", "/services"))
                    .Add("Stats")
                    .Add("Testimonial")
                    .Add("Footer", footer);
                builder.Page("about")
                    .Add("Navbar", nav)
                    .Add("Header", ("title", "About us"), ("subtitle", "Who we are and what we believe in."), ("showNavigation", false))
                    .Add("Text", content: LF("{0} was founded to help clients succeed. Tell your story here: how you started, what you do best and why people choose you.", name))
                    .Add("Stats")
                    .Add("Footer", footer);
                builder.Page("services")
                    .Add("Navbar", nav)
                    .Add("Header", ("title", "Services"), ("subtitle", "What we can do for you."), ("showNavigation", false))
                    .Add("Card", ("title", "Consulting"), ("description", "We look at your situation and suggest the best way forward."), ("linkText", "Get in touch"), ("linkUrl", "/contact"))
                    .Add("Card", ("title", "Implementation"), ("description", "Our team puts the plan into practice, on time and on budget."), ("linkText", "Get in touch"), ("linkUrl", "/contact"))
                    .Add("Accordion")
                    .Add("Footer", footer);
                builder.Page("contact")
                    .Add("Navbar", nav)
                    .Add("Header", ("title", "Contact"), ("subtitle", "We usually reply within one business day."), ("showNavigation", false))
                    .Add("ContactForm")
                    .Add("SocialLinks")
                    .Add("Footer", footer);
                break;

            case Portfolio:
                builder.Page("")
                    .Add("Navbar", ("brandName", name), ("menuItems", Links(("Work", "#gallery"), ("Clients", "#testimonials"), ("Contact", "#contact"))))
                    .Add("Hero", ("headline", LF("Hi, I'm {0}", name)), ("subheadline", "Designer and photographer. Here is some of my recent work."), ("ctaText", "View work"), ("ctaUrl", "#gallery"), ("height", "large"))
                    .Add("Gallery", ("title", "Selected work"))
                    .Add("Testimonial", ("quote", "Working together was a pleasure. The result exceeded our expectations."), ("author", "Happy Client"), ("role", "Founder, Studio"))
                    .Add("ContactForm", ("title", "Let's work together"))
                    .Add("SocialLinks")
                    .Add("Footer");
                break;

            case Shop:
                builder.Page("")
                    .Add("Navbar", ("brandName", name), ("ctaText", "Shop now"), ("ctaUrl", "#products"),
                        ("menuItems", Links(("Products", "#products"), ("Membership", "#pricing"), ("FAQ", "#faq"))))
                    .Add("Hero", ("headline", $"{name}"), ("subheadline", "Quality products, delivered to your door."), ("ctaText", "Browse products"), ("ctaUrl", "#products"))
                    .Add("ProductGrid")
                    .Add("Pricing", ("planName", "Membership"), ("price", "9"), ("features", JsonSerializer.Serialize(new[] { "Free delivery", "Member prices", "Early access to sales" })))
                    .Add("Accordion", ("items", JsonSerializer.Serialize(new[]
                    {
                        new { question = "How long does delivery take?", answer = "Usually 2-4 business days." },
                        new { question = "Can I return an item?", answer = "Yes, within 30 days of delivery." }
                    })))
                    .Add("Form", ("title", "Get our newsletter"))
                    .Add("Footer");
                break;

            default: // Blank
                break;
        }
    }

    private static string Links(params (string Text, string Url)[] links) =>
        JsonSerializer.Serialize(links.Select(l => new { text = l.Text, url = l.Url }));

    /// <summary>Adds blocks to pages, stacked top to bottom like in the editor.</summary>
    private sealed class Builder
    {
        private readonly ProjectModel _project;
        private readonly string? _language;
        private string _page = string.Empty;
        private double _y = 20;

        public Builder(ProjectModel project, string? language)
        {
            _project = project;
            _language = language;
        }

        public Builder Page(string slug)
        {
            _page = slug;
            _y = 20;
            return this;
        }

        public Builder Add(string type, params (string Key, object Value)[] props) => Add(type, null, props);

        public Builder Add(string type, string? content, params (string Key, object Value)[] props)
        {
            var block = new BlockModel
            {
                Type = type,
                Page = _page,
                Content = content ?? BlockDefaults.DefaultContent(type, _language),
                PositionX = 40,
                PositionY = _y,
                Width = 520,
                Height = 100
            };
            block.InitializeDefaultProperties(_language);
            foreach (var (key, value) in props)
                block.Properties[key] = ContentText.LocalizeEditable(_language, value) ?? value;
            _project.Blocks.Add(block);
            _y += 130;
            return this;
        }
    }
}
