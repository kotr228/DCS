namespace GrayCat.Core.Interfaces;

using GrayCat.Core.Models;
using GrayCat.Shared.Models;

public interface IProjectService
{
    Task<ProjectModel> CreateProjectAsync(string name, string author, ProjectType type);
    Task<ProjectModel?> LoadProjectAsync(string path);
    Task<bool> SaveProjectAsync(ProjectModel project, string path);
    Task<bool> DeleteProjectAsync(string path);
    Task<List<string>> GetRecentProjectsAsync();
}