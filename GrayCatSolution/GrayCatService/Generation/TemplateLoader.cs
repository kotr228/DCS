using GrayCat.Shared.Models;
using System.Reflection;
using System.Text.Json;

namespace GrayCat.Service.Generation
{
    public class TemplateLoader
    {
        private readonly ILogger<TemplateLoader> _logger;

        public TemplateLoader(ILogger<TemplateLoader> logger)
        {
            _logger = logger;
        }

        public JsonDocument? LoadTemplate(ProjectType type)
        {
            // Визначаємо ім'я ресурсу на основі типу проєкту
            string resourceName = $"GrayCat.Shared.Templates.{type}.json";
            _logger.LogInformation("Attempting to load embedded template: {ResourceName}", resourceName);

            try
            {
                var assembly = Assembly.GetAssembly(typeof(ProjectType)); // Отримуємо збірку, де лежать шаблони
                if (assembly == null)
                {
                    _logger.LogError("Could not find the 'GrayCat.Shared' assembly.");
                    return null;
                }

                using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        _logger.LogError("Template resource '{ResourceName}' not found in assembly.", resourceName);
                        return null;
                    }
                    return JsonDocument.Parse(stream);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load or parse template '{ResourceName}'.", resourceName);
                return null;
            }
        }
    }
}
