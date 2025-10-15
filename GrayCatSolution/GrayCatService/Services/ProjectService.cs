using GrayCat.Service.Generation;
using GrayCat.Service.Validation;
using GrayCat.Shared.Models;
using System.Diagnostics;

namespace GrayCat.Service.Services
{
    public class ProjectService
    {
        private readonly ILogger<ProjectService> _logger;
        private readonly ProjectValidator _validator;
        private readonly TemplateLoader _templateLoader;
        private readonly CodeGenerator _codeGenerator;

        public ProjectService(ILogger<ProjectService> logger, ProjectValidator validator, TemplateLoader templateLoader, CodeGenerator codeGenerator)
        {
            _logger = logger;
            _validator = validator;
            _templateLoader = templateLoader;
            _codeGenerator = codeGenerator;
        }

        public GenerationResult GenerateProject(ProjectGenerationRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("--- Starting project generation v0.2.5 for '{ProjectName}' ---", request.ProjectName);

            // 1. Validate the request
            var (isValid, errorMessage) = _validator.ValidateRequest(request.ProjectName, request.OutputPath);
            if (!isValid)
            {
                _logger.LogError("Validation failed: {ErrorMessage}", errorMessage);
                return new GenerationResult { Success = false, Message = errorMessage };
            }

            string projectRoot = Path.Combine(request.OutputPath, request.ProjectName);

            // 2. Check if the target directory is empty
            if (Directory.Exists(projectRoot) && Directory.EnumerateFileSystemEntries(projectRoot).Any())
            {
                _logger.LogWarning("Generation failed: Target directory '{ProjectRoot}' is not empty.", projectRoot);
                return new GenerationResult { Success = false, Message = $"Target directory is not empty: {projectRoot}" };
            }

            try
            {
                // 3. Load the template
                var template = _templateLoader.LoadTemplate(request.Type);
                if (template == null)
                {
                    throw new FileNotFoundException($"Template for '{request.Type}' not found or failed to load.");
                }

                // 4. Create root directory
                Directory.CreateDirectory(projectRoot);
                _logger.LogInformation("Root project directory created at '{ProjectRoot}'", projectRoot);

                // 5. Generate files and folders from the template
                var (files, dirs) = _codeGenerator.GenerateFromTemplate(projectRoot, request, template);

                // 6. Post-generation validation
                if (!File.Exists(Path.Combine(projectRoot, "package.json")) || !File.Exists(Path.Combine(projectRoot, "README.md")))
                {
                    throw new FileNotFoundException("Post-generation validation failed: Core project files (package.json, README.md) are missing.");
                }
                _logger.LogInformation("Post-generation validation passed.");

                stopwatch.Stop();
                _logger.LogInformation("--- Project generation finished successfully in {ElapsedMilliseconds}ms ---", stopwatch.ElapsedMilliseconds);

                return new GenerationResult
                {
                    Success = true,
                    Message = $"Project generated successfully!",
                    ProjectPath = projectRoot,
                    FilesCreated = files,
                    DirectoriesCreated = dirs + 1, // +1 for the root folder
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "A critical error occurred during project creation. Rolling back...");

                // Rollback on any failure
                Rollback(projectRoot);

                return new GenerationResult { Success = false, Message = $"Generation failed: {ex.Message}" };
            }
        }

        private void Rollback(string path)
        {
            _logger.LogWarning("Initiating rollback for directory: {Path}", path);
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    _logger.LogWarning("Rollback successful. Deleted directory: {Path}", path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback FAILED. Could not delete directory: {Path}", path);
            }
        }
    }
}