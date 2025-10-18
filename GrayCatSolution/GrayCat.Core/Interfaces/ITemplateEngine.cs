namespace GrayCat.Core.Interfaces;

using GrayCat.Core.Models;
using GrayCat.Shared.Models;

public interface ITemplateEngine
{
    Task<string> LoadTemplateAsync(ProjectType projectType, string templateName);
    string RenderTemplate(string template, Dictionary<string, object> variables);
    Task<List<string>> GetAvailableTemplatesAsync(ProjectType projectType);
}