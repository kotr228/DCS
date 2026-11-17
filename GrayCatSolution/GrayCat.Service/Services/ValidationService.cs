namespace GrayCat.Service.Services;

using GrayCat.Core.Generation;
using GrayCat.Core.Interfaces;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;

public class ValidationService : IProjectValidator
{
    public Task<(bool IsValid, List<string> Errors)> ValidateProjectAsync(ProjectModel project)
    {
        var errors = new List<string>();
        if (GenerationValidator.ValidateProjectName(project.ProjectName) is { } nameError)
            errors.Add(nameError);

        foreach (var block in project.Blocks ?? new List<BlockModel>())
        {
            if (string.IsNullOrWhiteSpace(block.Type))
                errors.Add($"Block {block.Id} has no type specified");
        }

        return Task.FromResult((errors.Count == 0, errors));
    }

    public Task<(bool IsValid, List<string> Errors)> ValidateGenerationRequestAsync(GenerationRequest request)
    {
        var errors = GenerationValidator.Validate(request);
        return Task.FromResult((errors.Count == 0, errors));
    }

    public bool ValidateProjectName(string projectName) =>
        GenerationValidator.ValidateProjectName(projectName) == null;

    public bool ValidateOutputPath(string path) =>
        GenerationValidator.ValidateOutputPath(path) == null;
}
