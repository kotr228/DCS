namespace GrayCat.Core.Interfaces;

using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;

public interface IProjectValidator
{
    Task<(bool IsValid, List<string> Errors)> ValidateProjectAsync(ProjectModel project);
    Task<(bool IsValid, List<string> Errors)> ValidateGenerationRequestAsync(GenerationRequest request);
    bool ValidateProjectName(string projectName);
    bool ValidateOutputPath(string path);
}