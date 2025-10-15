using GrayCat.Shared.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrayCat.Service.Generation
{
    public class CodeGenerator
    {
        private readonly ILogger<CodeGenerator> _logger;

        public CodeGenerator(ILogger<CodeGenerator> logger)
        {
            _logger = logger;
        }

        public (int files, int dirs) GenerateFromTemplate(string projectRoot, ProjectGenerationRequest request, JsonDocument template)
        {
            int filesCreated = 0;
            int directoriesCreated = 0;

            var variables = new Dictionary<string, string>
            {
                { "{{projectName}}", request.ProjectName },
                { "{{projectNameLowercase}}", request.ProjectName.ToLower() },
                { "{{author}}", request.Author },
                { "{{year}}", DateTime.UtcNow.Year.ToString() }
            };

            foreach (JsonElement element in template.RootElement.EnumerateArray())
            {
                string type = element.GetProperty("type").GetString() ?? "";
                string path = element.GetProperty("path").GetString() ?? "";

                if (string.IsNullOrEmpty(path)) continue;

                string fullPath = Path.Combine(projectRoot, path);

                if (type == "directory")
                {
                    Directory.CreateDirectory(fullPath);
                    directoriesCreated++;
                    _logger.LogInformation("Created directory: {Path}", path);
                }
                else if (type == "file")
                {
                    string content = element.GetProperty("content").GetString() ?? "";

                    // Заміна змінних
                    foreach (var variable in variables)
                    {
                        content = content.Replace(variable.Key, variable.Value);
                    }

                    // Переконуємось, що папка для файлу існує
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

                    File.WriteAllText(fullPath, content);
                    filesCreated++;
                    _logger.LogInformation("Created file: {Path}", path);
                }
            }
            return (filesCreated, directoriesCreated);
        }
    }
}
