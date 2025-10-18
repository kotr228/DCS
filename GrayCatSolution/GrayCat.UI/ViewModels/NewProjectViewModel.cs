namespace GrayCat.UI.ViewModels;

using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Shared.Models;
using System.Windows.Input;
using System.IO;

public class NewProjectViewModel : BaseViewModel
{
    private string _projectName = "MyWebsite";
    private string _author = Environment.UserName;
    private string _outputPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "GrayCatProjects");
    private ProjectType _selectedProjectType = ProjectType.React;

    public string ProjectName
    {
        get => _projectName;
        set => SetProperty(ref _projectName, value);
    }

    public string Author
    {
        get => _author;
        set => SetProperty(ref _author, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public ProjectType SelectedProjectType
    {
        get => _selectedProjectType;
        set => SetProperty(ref _selectedProjectType, value);
    }

    public Array ProjectTypes => Enum.GetValues(typeof(ProjectType));

    public GenerationRequest CreateRequest()
    {
        return new GenerationRequest
        {
            ProjectName = ProjectName,
            Author = Author,
            OutputPath = OutputPath,
            Type = SelectedProjectType,
            ProjectData = new ProjectModel
            {
                ProjectName = ProjectName,
                Author = Author,
                Type = SelectedProjectType
            }
        };
    }
}