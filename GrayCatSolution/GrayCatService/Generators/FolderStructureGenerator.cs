using GrayCat.Shared.Models;
using System.IO;

namespace GrayCat.Service.Generators
{
    public static class FolderStructureGenerator
    {
        public static void Create(ProjectGenerationRequest request)
        {
            string projectRoot = Path.Combine(request.OutputPath, request.ProjectName);
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "public"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "src/components"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "src/pages"));

            string packageJsonContent = $@"{{
  ""name"": ""{request.ProjectName.ToLower()}"",
  ""version"": ""0.1.0"",
  ""private"": true
}}";
            File.WriteAllText(Path.Combine(projectRoot, "package.json"), packageJsonContent);
        }
    }
}