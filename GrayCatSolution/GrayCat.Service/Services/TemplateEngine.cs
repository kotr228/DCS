namespace GrayCat.Service.Services;

using GrayCat.Core.Interfaces;
using GrayCat.Core.Models;
using GrayCat.Shared.Models;
using System.Text.RegularExpressions;

public class TemplateEngine : ITemplateEngine
{
    private readonly ILogger<TemplateEngine> _logger;
    private readonly string _templatesBasePath;

    public TemplateEngine(ILogger<TemplateEngine> logger, IConfiguration configuration)
    {
        _logger = logger;
        _templatesBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");

        // Ensure templates directory exists
        Directory.CreateDirectory(_templatesBasePath);
    }

    public async Task<string> LoadTemplateAsync(ProjectType projectType, string templateName)
    {
        var templatePath = Path.Combine(_templatesBasePath, projectType.ToString(), templateName);

        if (!File.Exists(templatePath))
        {
            _logger.LogWarning("Template not found: {TemplatePath}", templatePath);
            return string.Empty;
        }

        return await File.ReadAllTextAsync(templatePath);
    }

    public string RenderTemplate(string template, Dictionary<string, object> variables)
    {
        var result = template;

        foreach (var variable in variables)
        {
            var pattern = $@"{{{{{variable.Key}}}}}";
            result = Regex.Replace(result, pattern, variable.Value?.ToString() ?? string.Empty, RegexOptions.IgnoreCase);
        }

        return result;
    }

    public async Task<List<string>> GetAvailableTemplatesAsync(ProjectType projectType)
    {
        var typePath = Path.Combine(_templatesBasePath, projectType.ToString());

        if (!Directory.Exists(typePath))
        {
            return new List<string>();
        }

        var files = Directory.GetFiles(typePath, "*.*", SearchOption.AllDirectories);
        return files.Select(f => Path.GetRelativePath(typePath, f)).ToList();
    }
}
