namespace GrayCat.Tests;

using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Shared.Models;

/// <summary>Starter texts (templates, new blocks, form messages) follow the site's language.</summary>
public class ContentLanguageTests
{
    [Fact]
    public void Templates_InUkrainian_HaveUkrainianTexts()
    {
        foreach (var template in ProjectTemplates.Names)
        {
            var project = new ProjectModel { ProjectName = "Сайт", Settings = { [SettingKeys.Language] = "uk" } };
            ProjectTemplates.Apply(project, template, "uk");
            var html = string.Join("\n", ProjectScaffolder.BuildStaticSite(project).Values);

            Assert.DoesNotContain("Get Started", html);
            Assert.DoesNotContain("All rights reserved", html);
            if (project.Pages.Count > 1)
                Assert.Contains(project.Pages, p => p.Title == "Головна");
        }
    }

    [Fact]
    public void Templates_InEnglish_StayEnglish()
    {
        var project = new ProjectModel { ProjectName = "Site" };
        ProjectTemplates.Apply(project, ProjectTemplates.Landing, "en");
        var html = string.Join("\n", ProjectScaffolder.BuildStaticSite(project).Values);
        Assert.Contains("All rights reserved", html);
        Assert.DoesNotContain("Вітаємо", html);
    }

    [Fact]
    public void NewBlocks_GetDefaultsInTheSiteLanguage()
    {
        var block = new BlockModel { Type = "Hero" };
        block.InitializeDefaultProperties("uk");
        Assert.Contains(block.Properties.Values, v => v?.ToString() == "Вітаємо на нашому сайті");
        Assert.Equal("Напишіть тут свій текст.", BlockDefaults.DefaultContent("Text", "uk"));
        Assert.Equal("Write your text here.", BlockDefaults.DefaultContent("Text", "en"));
    }

    [Fact]
    public void Links_AndImages_AreNotTranslated()
    {
        Assert.Equal("Home", ContentText.T("en", "Home"));
        Assert.Equal("Головна", ContentText.T("uk", "Home"));
        Assert.Equal("Unknown sentence", ContentText.T("uk", "Unknown sentence"));
        Assert.Equal("[\"Головна\",\"Контакти\"]", ContentText.LocalizeEditable("uk", "[\"Home\",\"Contact\"]")?.ToString());
    }

    [Fact]
    public void FormMessages_FollowTheSiteLanguage()
    {
        var project = new ProjectModel { Settings = { [SettingKeys.Language] = "uk" } };
        var form = new BlockModel { Type = "Form" };
        form.InitializeDefaultProperties("uk");
        project.Blocks.Add(form);
        var html = ProjectScaffolder.BuildStaticSite(project)["index.html"];
        Assert.Contains("data-sending=\"Надсилання...\"", html);
    }
}
