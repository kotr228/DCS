namespace GrayCat.Core.Interfaces;

using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Shared.Models;

public interface ICodeGenerator
{
    Task<string> GenerateComponentCodeAsync(BlockModel block, ProjectType projectType);
    Task<string> GeneratePageCodeAsync(List<BlockModel> blocks, ProjectType projectType);
    Task<Dictionary<string, string>> GenerateProjectFilesAsync(ProjectModel project);
}