namespace GrayCat.Service.Services;

using GrayCat.Core.Interfaces;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Shared.Models;
using System.Diagnostics;

public class ProjectGenerationService : IGenerationService
{
    private readonly ILogger<ProjectGenerationService> _logger;
    private readonly ICodeGenerator _codeGenerator;
    private readonly ITemplateEngine _templateEngine;
    private readonly IProjectValidator _validator;
    private readonly Dictionary<string, GenerationStatus> _activeGenerations = new();

    public ProjectGenerationService(
        ILogger<ProjectGenerationService> logger,
        ICodeGenerator codeGenerator,
        ITemplateEngine templateEngine,
        IProjectValidator validator)
    {
        _logger = logger;
        _codeGenerator = codeGenerator;
        _templateEngine = templateEngine;
        _validator = validator;
    }

    public async Task<GenerationResult> GenerateProjectAsync(GenerationRequest request)
    {
        var sw = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString();

        _logger.LogInformation("Starting project generation: {ProjectName}", request.ProjectName);

        try
        {
            // 1. Validation
            var validationResult = await ValidateBeforeGenerationAsync(request);
            if (!validationResult.Success)
            {
                return validationResult;
            }

            // 2. Create project directory
            var projectPath = Path.Combine(request.OutputPath, request.ProjectName);
            Directory.CreateDirectory(projectPath);

            var filesCreated = 0;
            var dirsCreated = 1;

            // 3. Generate project structure based on type
            switch (request.Type)
            {
                case ProjectType.React:
                    (filesCreated, dirsCreated) = await GenerateReactProjectAsync(projectPath, request);
                    break;
                case ProjectType.NextJs:
                    (filesCreated, dirsCreated) = await GenerateNextJsProjectAsync(projectPath, request);
                    break;
                case ProjectType.NodeJsApi:
                    (filesCreated, dirsCreated) = await GenerateNodeApiProjectAsync(projectPath, request);
                    break;
                case ProjectType.ReactNative:
                    (filesCreated, dirsCreated) = await GenerateReactNativeProjectAsync(projectPath, request);
                    break;
            }

            // 4. Generate components from blocks
            if (request.ProjectData?.Blocks.Any() == true)
            {
                var componentsPath = Path.Combine(projectPath, "src", "components");
                Directory.CreateDirectory(componentsPath);
                dirsCreated++;

                foreach (var block in request.ProjectData.Blocks)
                {
                    var componentCode = await _codeGenerator.GenerateComponentCodeAsync(block, request.Type);
                    var componentPath = Path.Combine(componentsPath, $"{block.Type}Block.jsx");
                    await File.WriteAllTextAsync(componentPath, componentCode);
                    filesCreated++;
                }
            }

            // 5. Save project metadata
            await SaveProjectMetadataAsync(projectPath, request);
            filesCreated++;

            sw.Stop();

            _logger.LogInformation("Project generated successfully in {Duration}ms", sw.ElapsedMilliseconds);

            return new GenerationResult
            {
                Success = true,
                Message = "Project generated successfully",
                ProjectPath = projectPath,
                FilesCreated = filesCreated,
                DirectoriesCreated = dirsCreated,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating project");

            return new GenerationResult
            {
                Success = false,
                Message = $"Generation failed: {ex.Message}",
                Errors = new List<string> { ex.Message }
            };
        }
    }

    public async Task<GenerationResult> ValidateBeforeGenerationAsync(GenerationRequest request)
    {
        var result = new GenerationResult();
        var errors = new List<string>();

        // Validate project name
        if (!_validator.ValidateProjectName(request.ProjectName))
        {
            errors.Add("Invalid project name");
        }

        // Validate output path
        if (!_validator.ValidateOutputPath(request.OutputPath))
        {
            errors.Add("Invalid or inaccessible output path");
        }

        // Check if directory exists and is empty
        var targetPath = Path.Combine(request.OutputPath, request.ProjectName);
        if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
        {
            errors.Add("Target directory already exists and is not empty");
        }

        result.Success = !errors.Any();
        result.Errors = errors;
        result.Message = errors.Any() ? string.Join("; ", errors) : "Validation passed";

        return await Task.FromResult(result);
    }

    public async Task<string> GetGenerationStatusAsync(string requestId)
    {
        if (_activeGenerations.TryGetValue(requestId, out var status))
        {
            return status.ToString();
        }
        return "Unknown";
    }

    private async Task<(int files, int dirs)> GenerateReactProjectAsync(string projectPath, GenerationRequest request)
    {
        var files = 0;
        var dirs = 0;

        // Create directory structure
        var directories = new[] { "src", "public", "src/components", "src/pages", "src/styles" };
        foreach (var dir in directories)
        {
            Directory.CreateDirectory(Path.Combine(projectPath, dir));
            dirs++;
        }

        // Generate package.json
        var packageJson = _templateEngine.RenderTemplate(
            await _templateEngine.LoadTemplateAsync(ProjectType.React, "package.json"),
            new Dictionary<string, object>
            {
                { "projectName", request.ProjectName.ToLower() },
                { "author", request.Author }
            });
        await File.WriteAllTextAsync(Path.Combine(projectPath, "package.json"), packageJson);
        files++;

        // Generate index.html
        var indexHtml = _templateEngine.RenderTemplate(
            await _templateEngine.LoadTemplateAsync(ProjectType.React, "index.html"),
            new Dictionary<string, object> { { "projectName", request.ProjectName } });
        await File.WriteAllTextAsync(Path.Combine(projectPath, "public", "index.html"), indexHtml);
        files++;

        // Generate App.js
        var appJs = await GenerateAppJsFromBlocks(request.ProjectData);
        await File.WriteAllTextAsync(Path.Combine(projectPath, "src", "App.js"), appJs);
        files++;

        return (files, dirs);
    }

    private async Task<(int files, int dirs)> GenerateNextJsProjectAsync(string projectPath, GenerationRequest request)
    {
        var files = 0;
        var dirs = 0;

        var directories = new[] { "pages", "components", "public", "styles" };
        foreach (var dir in directories)
        {
            Directory.CreateDirectory(Path.Combine(projectPath, dir));
            dirs++;
        }

        var packageJson = _templateEngine.RenderTemplate(
            await _templateEngine.LoadTemplateAsync(ProjectType.NextJs, "package.json"),
            new Dictionary<string, object>
            {
                { "projectName", request.ProjectName.ToLower() },
                { "author", request.Author }
            });
        await File.WriteAllTextAsync(Path.Combine(projectPath, "package.json"), packageJson);
        files++;

        return (files, dirs);
    }

    private async Task<(int files, int dirs)> GenerateNodeApiProjectAsync(string projectPath, GenerationRequest request)
    {
        var files = 0;
        var dirs = 0;

        var directories = new[] { "src", "src/routes", "src/controllers", "src/models" };
        foreach (var dir in directories)
        {
            Directory.CreateDirectory(Path.Combine(projectPath, dir));
            dirs++;
        }

        // If SQLite requested, add database setup
        if (request.UseSQLite)
        {
            Directory.CreateDirectory(Path.Combine(projectPath, "database"));
            dirs++;
        }

        return (files, dirs);
    }

    private async Task<(int files, int dirs)> GenerateReactNativeProjectAsync(string projectPath, GenerationRequest request)
    {
        var files = 0;
        var dirs = 0;

        var directories = new[] { "src", "src/screens", "src/components", "src/navigation" };
        foreach (var dir in directories)
        {
            Directory.CreateDirectory(Path.Combine(projectPath, dir));
            dirs++;
        }

        return (files, dirs);
    }

    private async Task<string> GenerateAppJsFromBlocks(ProjectModel? projectData)
    {
        if (projectData?.Blocks == null || !projectData.Blocks.Any())
        {
            return @"import React from 'react';
import './App.css';

function App() {
  return (
    <div className=""App"">
      <h1>Welcome to Your New Project</h1>
      <p>Start by adding blocks in GrayCat!</p>
    </div>
  );
}

export default App;";
        }

        var imports = string.Join("\n", projectData.Blocks
            .Select(b => $"import {b.Type}Block from './components/{b.Type}Block';"));

        var components = string.Join("\n      ", projectData.Blocks
            .Select(b => $"<{b.Type}Block {{...{b.Id}Props}} />"));

        return $@"import React from 'react';
import './App.css';
{imports}

function App() {{
  return (
    <div className=""App"">
      {components}
    </div>
  );
}}

export default App;";
    }

    private async Task SaveProjectMetadataAsync(string projectPath, GenerationRequest request)
    {
        var metadata = new
        {
            projectName = request.ProjectName,
            author = request.Author,
            type = request.Type.ToString(),
            createdAt = DateTime.UtcNow,
            version = "1.0.0",
            blocks = request.ProjectData?.Blocks ?? new List<BlockModel>()
        };

        var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(Path.Combine(projectPath, "graycat.project.json"), json);
    }

    private enum GenerationStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed
    }
}