namespace GrayCat.UI.ViewModels;

using GrayCat.UI.Localization;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Shared.Models;
using System.IO;

public class NewProjectViewModel : BaseViewModel
{
    private string _projectName = "MyWebsite";
    private string _author = Environment.UserName;
    private string _outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GrayCatProjects");
    private ProjectType _selectedProjectType = ProjectType.StaticHtml;
    private string _template = ProjectTemplates.Landing;
    private bool _useSqlite;

    public string ProjectName
    {
        get => _projectName;
        set { if (SetProperty(ref _projectName, value)) OnValidationChanged(); }
    }

    public string Author
    {
        get => _author;
        set => SetProperty(ref _author, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set { if (SetProperty(ref _outputPath, value)) OnValidationChanged(); }
    }

    public ProjectType SelectedProjectType
    {
        get => _selectedProjectType;
        set
        {
            if (SetProperty(ref _selectedProjectType, value))
            {
                OnPropertyChanged(nameof(ProjectTypeDescription));
                OnPropertyChanged(nameof(IsNodeApi));
            }
        }
    }

    public IReadOnlyList<string> Templates => ProjectTemplates.Names;

    public string Template
    {
        get => _template;
        set
        {
            if (SetProperty(ref _template, value))
                OnPropertyChanged(nameof(TemplateDescription));
        }
    }

    public string TemplateDescription => Loc.T(ProjectTemplates.Describe(Template));

    public bool UseSqlite
    {
        get => _useSqlite;
        set => SetProperty(ref _useSqlite, value);
    }

    public bool IsNodeApi => SelectedProjectType == ProjectType.NodeJsApi;

    public Array ProjectTypes => Enum.GetValues(typeof(ProjectType));

    public string ProjectTypeDescription => SelectedProjectType switch
    {
        ProjectType.StaticHtml => Loc.T("Plain HTML/CSS/JS. Open index.html - no tools needed."),
        ProjectType.React => Loc.T("React app built with Vite. Needs Node.js: npm install, npm run dev."),
        ProjectType.NextJs => Loc.T("Next.js app with a built-in API for forms. Needs Node.js."),
        ProjectType.NodeJsApi => Loc.T("Express server that serves your site and stores form submissions."),
        ProjectType.ReactNative => Loc.T("Mobile app with Expo. Run npx expo start and open it in Expo Go."),
        _ => ""
    };

    /// <summary>First problem with the current input, or empty when it is fine.</summary>
    public string ValidationError
    {
        get
        {
            var error = GenerationValidator.ValidateProjectName(ProjectName) ?? GenerationValidator.ValidateOutputPath(OutputPath);
            if (error != null)
                return Loc.T(error);

            var document = Path.Combine(OutputPath, ProjectName.Trim() + GrayCatDocument.Extension);
            if (File.Exists(document))
                return Loc.F("{0} already exists. Open it with File → Open Project, or choose another name.", document);
            var code = Path.Combine(OutputPath, ProjectName.Trim());
            if (Directory.Exists(code) && Directory.EnumerateFileSystemEntries(code).Any() &&
                !File.Exists(Path.Combine(code, ProjectScaffolder.OutputMarker)) && !File.Exists(Path.Combine(code, ProjectScaffolder.ProjectFileName)))
                return Loc.F("The folder {0} (where the code would go) already has other files. Choose another name.", code);
            return string.Empty;
        }
    }

    public bool IsValid => ValidationError.Length == 0;

    public string TargetFolderPreview
    {
        get
        {
            try { return Path.Combine(OutputPath, ProjectName.Trim() + GrayCatDocument.Extension); }
            catch (ArgumentException) { return string.Empty; }
        }
    }

    public GenerationRequest CreateRequest() => new()
    {
        ProjectName = ProjectName.Trim(),
        Author = Author.Trim(),
        OutputPath = OutputPath.Trim(),
        Type = SelectedProjectType,
        UseSQLite = IsNodeApi && UseSqlite,
        ProjectData = new ProjectModel
        {
            ProjectName = ProjectName.Trim(),
            Author = Author.Trim(),
            Type = SelectedProjectType
        }
    };

    private void OnValidationChanged()
    {
        OnPropertyChanged(nameof(ValidationError));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(TargetFolderPreview));
    }
}
