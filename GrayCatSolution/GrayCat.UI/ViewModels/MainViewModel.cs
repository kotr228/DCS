namespace GrayCat.UI.ViewModels;

using GrayCat.UI.Localization;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Core.Serialization;
using GrayCat.Shared.Models;
using GrayCat.UI.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

/// <summary>Outcome of "Run App": what to open (URL or file) or why it failed.</summary>
public sealed record RunResult(bool Success, string Message, string? Target = null);

public class MainViewModel : BaseViewModel
{
    private const int MaxHistory = 100;

    private readonly ServiceCommunicator _serviceCommunicator = new();
    private readonly ProjectManager _projectManager;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly AppRunner _runner = new();
    private bool _isAppRunning;

    private bool _isProjectOpen;
    private bool _isBusy;
    private bool _hasUnsavedChanges;
    private string _statusMessage = Loc.T("Ready");
    private ProjectModel? _currentProject;
    private OpenedProject? _opened;
    private BlockViewModel? _selectedBlock;
    private BlockModel? _clipboard;
    private string? _historyBaseline;
    private PageInfo? _currentPage;

    public MainViewModel() : this(new ProjectManager())
    {
    }

    public MainViewModel(ProjectManager projectManager)
    {
        _projectManager = projectManager;
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _autoSaveTimer.Tick += async (_, _) =>
        {
            _autoSaveTimer.Stop();
            CommitHistory();
            if (await SaveProjectAsync())
                await SyncRunningAppAsync();
        };

        _runner.OutputReceived += line => RunOutput?.Invoke(line);
        _runner.Exited += code => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            IsAppRunning = false;
            StatusMessage = Loc.F("The app stopped (exit code {0}). See Project → Run Log.", code);
        });
    }

    /// <summary>The page content changed (block added, edited, removed, or a project was opened).</summary>
    public event EventHandler? ContentChanged;

    /// <summary>A line of output from npm / the dev server (raised on a background thread).</summary>
    public event Action<string>? RunOutput;

    public bool IsAppRunning
    {
        get => _isAppRunning;
        private set => SetProperty(ref _isAppRunning, value);
    }

    public string RunLog => _runner.Log;

    /// <summary>Blocks of the page being edited.</summary>
    public ObservableCollection<BlockViewModel> Blocks { get; } = new();

    public ObservableCollection<PageInfo> Pages { get; } = new();

    /// <summary>The page shown in the editor. Changing it swaps the blocks on the canvas.</summary>
    public PageInfo? CurrentPage
    {
        get => _currentPage;
        set
        {
            if (value == null || ReferenceEquals(value, _currentPage) || _currentProject == null)
                return;
            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPageSlug));
            RebuildBlocks(_currentProject.Blocks, selectId: null);
            ContentChanged?.Invoke(this, EventArgs.Empty);
            StatusMessage = Loc.F("Editing page: {0}", value.Title);
        }
    }

    public string CurrentPageSlug => _currentPage?.Slug ?? string.Empty;
    public ObservableCollection<string> RecentProjects { get; } = new();

    public bool IsProjectOpen
    {
        get => _isProjectOpen;
        private set => SetProperty(ref _isProjectOpen, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public BlockViewModel? SelectedBlock
    {
        get => _selectedBlock;
        set
        {
            if (_selectedBlock != null) _selectedBlock.IsSelected = false;
            SetProperty(ref _selectedBlock, value);
            if (_selectedBlock != null) _selectedBlock.IsSelected = true;
            OnPropertyChanged(nameof(IsBlockSelected));
        }
    }

    public bool IsBlockSelected => SelectedBlock != null;
    public bool CanPaste => _clipboard != null && IsProjectOpen;
    public bool CanUndo => IsProjectOpen && (_undo.Count > 0 || HasUnsavedChanges);
    public bool CanRedo => IsProjectOpen && _redo.Count > 0;
    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>Canvas grows with its content so blocks low on the page stay reachable by scrolling.</summary>
    public double CanvasHeight => Math.Max(900, Blocks.Count == 0 ? 0 : Blocks.Max(b => b.PositionY + b.Height) + 300);

    public ProjectModel? CurrentProject => _currentProject;
    public string ProjectName => _currentProject?.ProjectName ?? Loc.T("No Project");
    public string ProjectAuthor => _currentProject?.Author ?? "";
    public string ProjectTypeName => _currentProject == null ? "" : ProjectTypeNames.Display(_currentProject.Type);

    /// <summary>Language of the site's content (Project → Settings); new blocks get their starter texts in it.</summary>
    public string ContentLanguage =>
        _currentProject != null && _currentProject.Settings.TryGetValue(SettingKeys.Language, out var lang) ? lang : "en";
    /// <summary>Folder for the generated code (next to the .graycat file).</summary>
    public string? ProjectFolder => _opened?.OutputFolder;

    /// <summary>The .graycat file being edited.</summary>
    public string? DocumentPath => _opened?.DocumentPath;

    /// <summary>Unpacked copy of the .graycat (holds the assets folder) while the project is open.</summary>
    public string? WorkFolder => _opened?.WorkFolder;

    // ========== Project lifecycle ==========

    public async Task<GenerationResult> CreateNewProjectAsync(GenerationRequest request, string template)
    {
        if (!await FlushAsync())
            return new GenerationResult { Success = false, Message = Loc.T("The current project could not be saved.") };

        var errors = new List<string>();
        if (GenerationValidator.ValidateProjectName(request.ProjectName) is { } nameError) errors.Add(nameError);
        if (GenerationValidator.ValidateOutputPath(request.OutputPath) is { } pathError) errors.Add(pathError);
        var documentPath = errors.Count == 0 ? Path.Combine(request.OutputPath, request.ProjectName.Trim() + GrayCatDocument.Extension) : "";
        if (errors.Count == 0 && File.Exists(documentPath))
            errors.Add(Loc.F("{0} already exists.", documentPath));
        if (errors.Count > 0)
            return new GenerationResult { Success = false, Message = string.Join("\n", errors), Errors = errors };

        var project = request.ProjectData ?? new ProjectModel();
        project.ProjectName = request.ProjectName.Trim();
        project.Author = request.Author;
        project.Type = request.Type;
        if (request.UseSQLite)
            project.Settings["node.sqlite"] = "true";
        if (project.Blocks.Count == 0)
        {
            // The site's content language starts as the interface language; starter texts follow it.
            if (!project.Settings.ContainsKey(SettingKeys.Language))
                project.Settings[SettingKeys.Language] = Loc.Instance.Lang;
            ProjectTemplates.Apply(project, template, project.Settings[SettingKeys.Language]);
        }

        // A new project is just the .graycat file; code appears only after Generate / Run / Build.
        try
        {
            var opened = await _projectManager.CreateAsync(project, documentPath);
            await ShowProjectAsync(opened);
            StatusMessage = Loc.F("Created {0}", documentPath);
            return new GenerationResult { Success = true, Message = Loc.T("Project created"), ProjectPath = documentPath };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new GenerationResult { Success = false, Message = ex.Message, Errors = { ex.Message } };
        }
    }

    /// <summary>Opens a .graycat file (or a GrayCat 1.0 graycat.project.json, which is converted).</summary>
    public async Task LoadProjectAsync(string projectFilePath)
    {
        var opened = await _projectManager.OpenAsync(projectFilePath);
        await ShowProjectAsync(opened);
        if (opened.ConvertedFromLegacy)
            StatusMessage = Loc.F("Converted to the new format: {0}", opened.DocumentPath);
    }

    private async Task ShowProjectAsync(OpenedProject opened)
    {
        if (_runner.RunningFolder != null && _runner.RunningFolder != opened.OutputFolder)
            StopApp();

        var previous = _opened;
        _opened = opened;
        _projectManager.Close(previous);

        var project = opened.Project;
        _currentProject = project;
        ProjectPages.Normalize(project);
        ResetPages(project.Pages[0]);
        RebuildBlocks(project.Blocks, selectId: null);

        _undo.Clear();
        _redo.Clear();
        _historyBaseline = Snapshot();

        IsProjectOpen = true;
        HasUnsavedChanges = false;
        StatusMessage = Loc.F("Opened {0}", opened.DocumentPath);
        RaiseProjectChanged();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        await RefreshRecentProjectsAsync();
    }

    /// <returns>false if saving failed.</returns>
    public async Task<bool> SaveProjectAsync()
    {
        _autoSaveTimer.Stop();
        if (_currentProject == null || _opened == null)
            return true;

        try
        {
            await _projectManager.SaveAsync(_opened);
            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(CanUndo));
            StatusMessage = Loc.F("Saved at {0:HH:mm:ss}", DateTime.Now);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Loc.F("Save failed: {0}", ex.Message);
            return false;
        }
    }

    /// <summary>Saves the project as another .graycat file and continues editing that one.</summary>
    /// <returns>false if saving failed (the project stays attached to its old file).</returns>
    public async Task<bool> SaveProjectAsAsync(string documentPath)
    {
        _autoSaveTimer.Stop();
        if (_currentProject == null || _opened == null)
            return false;
        if (string.Equals(Path.GetFullPath(documentPath), _opened.DocumentPath, StringComparison.OrdinalIgnoreCase))
            return await SaveProjectAsync();

        // The code folder follows the file name, so a running app belongs to the old copy.
        if (_runner.IsRunning)
            StopApp();

        // A project named after its file keeps following it ("Shop" → "Shop 2").
        var oldName = _currentProject.ProjectName;
        if (oldName == Path.GetFileNameWithoutExtension(_opened.DocumentPath))
            _currentProject.ProjectName = Path.GetFileNameWithoutExtension(documentPath);
        try
        {
            _opened = await _projectManager.SaveAsAsync(_opened, documentPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _currentProject.ProjectName = oldName;
            StatusMessage = Loc.F("Save failed: {0}", ex.Message);
            return false;
        }

        HasUnsavedChanges = false;
        StatusMessage = Loc.F("Saved as {0}", _opened.DocumentPath);
        RaiseProjectChanged();
        await RefreshRecentProjectsAsync();
        return true;
    }

    /// <summary>Saves pending changes, if any.</summary>
    public Task<bool> FlushAsync() => HasUnsavedChanges ? SaveProjectAsync() : Task.FromResult(true);

    public async Task RefreshRecentProjectsAsync()
    {
        var recent = await _projectManager.GetRecentProjectsAsync();
        RecentProjects.Clear();
        foreach (var path in recent)
            RecentProjects.Add(path);
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    /// <summary>Applies the project settings dialog. Returns true when the project type changed.</summary>
    public bool UpdateSettings(string name, string author, string description, ProjectType type)
    {
        if (_currentProject == null) return false;

        var typeChanged = _currentProject.Type != type;
        if (typeChanged)
            StopApp();
        _currentProject.ProjectName = string.IsNullOrWhiteSpace(name) ? _currentProject.ProjectName : name.Trim();
        _currentProject.Author = author.Trim();
        _currentProject.Type = type;
        if (string.IsNullOrWhiteSpace(description))
            _currentProject.Settings.Remove(SettingKeys.Description);
        else
            _currentProject.Settings[SettingKeys.Description] = description.Trim();

        RaiseProjectChanged();
        MarkChanged();
        return typeChanged;
    }

    // ========== Code generation, preview, export ==========

    public async Task<GenerationResult> GenerateCodeAsync()
    {
        if (_currentProject == null || ProjectFolder == null)
            return new GenerationResult { Success = false, Message = Loc.T("Open or create a project first.") };

        CommitHistory();
        if (!await SaveProjectAsync())
            return new GenerationResult { Success = false, Message = StatusMessage };

        var request = CodeRequest(ProjectFolder);

        IsBusy = true;
        StatusMessage = Loc.T("Generating code...");
        try
        {
            var result = await _serviceCommunicator.GenerateProjectAsync(request);
            StatusMessage = result.Success
                ? Loc.F("Code generated: {0} files ({1}, {2} ms)", result.FilesCreated, _serviceCommunicator.LastEngine, result.DurationMs)
                : Loc.T("Code generation failed");
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Runs the generated project for real: static sites open from disk, the others get
    /// npm install (first time) and their dev server. Edits then flow into the running app.
    /// </summary>
    public async Task<RunResult> RunAppAsync()
    {
        if (_currentProject == null || ProjectFolder == null)
            return new RunResult(false, Loc.T("Open a project first."));

        var folder = ProjectFolder;
        var type = _currentProject.Type;

        if (type != ProjectType.StaticHtml && !AppRunner.IsNodeInstalled())
            return new RunResult(false, Loc.T("Node.js is needed to run this project but was not found.\n\n") +
                                        Loc.T("Install the LTS version from https://nodejs.org, restart GrayCat Studio and try again.\n") +
                                        Loc.T("Meanwhile, Project → Static HTML Preview shows the page without Node.js."));

        if (_runner.IsRunning && _runner.RunningFolder == folder && _runner.Url != null)
            return new RunResult(true, Loc.T("Already running"), _runner.Url);

        var generated = await GenerateCodeAsync();
        if (!generated.Success)
            return new RunResult(false, generated.Message);

        if (type == ProjectType.StaticHtml)
            return new RunResult(true, Loc.T("Opened index.html"), Path.Combine(folder, "index.html"));

        IsBusy = true;
        try
        {
            if (AppRunner.NeedsInstall(folder))
            {
                StatusMessage = Loc.T("Installing packages with npm (the first run can take a few minutes)...");
                if (!await _runner.InstallAsync(folder))
                {
                    StatusMessage = Loc.T("npm install failed");
                    return new RunResult(false, Loc.T("npm install failed. Open Project → Run Log for details."));
                }
            }

            StatusMessage = Loc.T("Starting the app...");
            var url = await _runner.StartAsync(folder, type, TimeSpan.FromMinutes(3));
            IsAppRunning = _runner.IsRunning;
            if (url == null)
            {
                StatusMessage = Loc.T("The app did not start");
                return new RunResult(false, Loc.T("The app did not start. Open Project → Run Log for details."));
            }

            StatusMessage = Loc.F("App running at {0}. Your edits update it automatically.", url);
            return new RunResult(true, Loc.T("Running"), url);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Generates the code and creates the version to publish (dist/, .next/ ...).</summary>
    public async Task<RunResult> BuildForProductionAsync()
    {
        if (_currentProject == null || ProjectFolder == null)
            return new RunResult(false, Loc.T("Open a project first."));

        var folder = ProjectFolder;
        var type = _currentProject.Type;
        if (type is not (ProjectType.StaticHtml or ProjectType.NodeJsApi) && !AppRunner.IsNodeInstalled())
            return new RunResult(false, Loc.T("Node.js is needed to build this project. Install the LTS version from https://nodejs.org and restart GrayCat Studio."));

        var generated = await GenerateCodeAsync();
        if (!generated.Success)
            return new RunResult(false, generated.Message);

        switch (type)
        {
            case ProjectType.StaticHtml:
                return new RunResult(true, Loc.T("Static sites need no build. Upload the .html files, styles.css, script.js and the assets folder to any web host."), folder);
            case ProjectType.NodeJsApi:
                return new RunResult(true, Loc.T("Node.js projects run as they are: copy the folder to the server, run npm install --omit=dev and npm start."), folder);
        }

        IsBusy = true;
        try
        {
            if (AppRunner.NeedsInstall(folder))
            {
                StatusMessage = Loc.T("Installing packages with npm...");
                if (!await _runner.InstallAsync(folder))
                    return new RunResult(false, Loc.T("npm install failed. Open Project → Run Log for details."));
            }

            StatusMessage = Loc.T("Building for production...");
            if (!await _runner.BuildAsync(folder, type))
            {
                StatusMessage = Loc.T("Build failed");
                return new RunResult(false, Loc.T("The build failed. Open Project → Run Log for details."));
            }

            var output = AppRunner.OutputFolder(folder, type);
            StatusMessage = Loc.F("Production build ready in {0}", output);
            var message = type switch
            {
                ProjectType.NextJs => Loc.F("Build ready in {0}.\nRun it on a server with: npm start", output),
                _ => Loc.F("Build ready in {0}.\nUpload the contents of this folder to any static web host ", output) +
                     Loc.T("(set it to serve index.html for unknown paths so page links like /about keep working).")
            };
            return new RunResult(true, message, type == ProjectType.NextJs ? folder : output);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void StopApp()
    {
        _runner.Stop();
        IsAppRunning = false;
        StatusMessage = Loc.T("App stopped");
    }

    /// <summary>Stops background processes; called when the editor closes.</summary>
    public void Shutdown()
    {
        _runner.Dispose();
        _projectManager.Close(_opened);
    }

    /// <summary>After a save, rewrites the generated files the running dev server watches.</summary>
    private async Task SyncRunningAppAsync()
    {
        if (!_runner.IsRunning || _currentProject == null || ProjectFolder == null || _runner.RunningFolder != ProjectFolder)
            return;

        var result = await ProjectGenerator.GenerateAsync(CodeRequest(ProjectFolder));
        StatusMessage = result.Success
            ? Loc.F("Saved. Running app updated at {0:HH:mm:ss}", DateTime.Now)
            : Loc.F("Saved, but the running app could not be updated: {0}", result.Message);
    }

    /// <summary>Request that writes the code for the open project into <paramref name="folder"/>.</summary>
    private GenerationRequest CodeRequest(string folder) => new()
    {
        ProjectName = Path.GetFileName(folder),
        OutputPath = Path.GetDirectoryName(folder) ?? folder,
        Author = _currentProject!.Author,
        Type = _currentProject.Type,
        ProjectData = _currentProject,
        UseSQLite = _currentProject.Settings.TryGetValue("node.sqlite", out var sqlite) && sqlite == "true",
        AssetsDirectory = WorkFolder,
        Overwrite = Directory.Exists(folder)
    };

    public async Task<string?> WritePreviewAsync()
    {
        if (_currentProject == null) return null;
        var path = await _projectManager.WritePreviewAsync(_currentProject, WorkFolder, CurrentPageSlug);
        StatusMessage = Loc.T("Preview opened in your browser");
        return path;
    }

    public async Task<string?> ExportStaticSiteAsync(string folder)
    {
        if (_currentProject == null) return null;
        var path = await _projectManager.ExportStaticSiteAsync(_currentProject, folder, WorkFolder);
        StatusMessage = Loc.F("Exported to {0}", folder);
        return path;
    }

    /// <summary>Copies a local file into the project's assets folder; returns the path to store in a block.</summary>
    public string ImportAsset(string file)
    {
        if (WorkFolder == null)
            throw new InvalidOperationException(Loc.T("Open a project first."));
        var path = ProjectAssets.Import(WorkFolder, file);
        MarkChanged(); // the next save packs the new file into the .graycat
        StatusMessage = Loc.F("Added {0} to the project", path);
        return path;
    }

    // ========== Blocks ==========

    /// <summary>Adds a block right after <paramref name="after"/> in the page order (or at the end).</summary>
    public BlockViewModel? AddBlockAfter(string blockType, BlockViewModel? after)
    {
        if (_currentProject == null)
            return null;
        if (after == null || !Blocks.Contains(after))
        {
            var y = Blocks.Count == 0 ? 20 : Blocks.Max(b => b.PositionY + b.Height) + 30;
            return AddBlock(blockType, 40, Math.Round(y / 10) * 10);
        }

        const double gap = 30, height = 100;
        var ordered = Blocks.OrderBy(b => b.PositionY).ThenBy(b => b.PositionX).ToList();
        var later = ordered.Skip(ordered.IndexOf(after) + 1).ToList();
        var newY = after.PositionY + after.Height + gap;

        CommitHistory();
        // Push everything that follows down so the new block fits below the selected one.
        foreach (var block in later)
            block.PositionY += height + gap;
        return AddBlock(blockType, after.PositionX, newY);
    }

    public BlockViewModel? AddBlock(string blockType, double x, double y)
    {
        if (_currentProject == null)
            return null;

        var model = new BlockModel
        {
            Type = blockType,
            Content = BlockDefaults.DefaultContent(blockType, ContentLanguage),
            Page = CurrentPageSlug,
            PositionX = Math.Max(0, x),
            PositionY = Math.Max(0, y),
            Width = 260,
            Height = 100
        };
        model.InitializeDefaultProperties(ContentLanguage);

        return Structural(Loc.F("Added {0} block", Loc.Block(blockType)), () =>
        {
            _currentProject.Blocks.Add(model);
            var vm = AddBlockViewModel(model);
            SelectedBlock = vm;
            return vm;
        });
    }

    public void RemoveBlock(BlockViewModel block)
    {
        if (_currentProject == null)
            return;

        Structural(Loc.F("Deleted {0} block", Loc.Block(block.Type)), () =>
        {
            _currentProject.Blocks.Remove(block.Model);
            block.Changed -= OnBlockChanged;
            Blocks.Remove(block);
            if (SelectedBlock == block)
                SelectedBlock = null;
            return block;
        });
    }

    public BlockViewModel? DuplicateBlock(BlockViewModel block) => InsertCopy(block.Model);

    public void CopySelected()
    {
        if (SelectedBlock == null) return;
        _clipboard = SelectedBlock.Model.Clone();
        OnPropertyChanged(nameof(CanPaste));
        StatusMessage = Loc.F("{0} block copied", Loc.Block(SelectedBlock.Type));
    }

    public void CutSelected()
    {
        if (SelectedBlock == null) return;
        CopySelected();
        RemoveBlock(SelectedBlock);
    }

    public BlockViewModel? Paste() => _clipboard == null ? null : InsertCopy(_clipboard);

    /// <summary>Stacks all blocks in one column, keeping their current page order.</summary>
    public void ArrangeBlocks()
    {
        if (_currentProject == null || Blocks.Count == 0)
            return;

        Structural(Loc.T("Blocks arranged"), () =>
        {
            var y = 20.0;
            foreach (var block in Blocks.OrderBy(b => b.PositionY).ThenBy(b => b.PositionX).ToList())
            {
                block.PositionX = 40;
                block.PositionY = y;
                y += block.Height + 30;
            }
            return true;
        });
    }

    /// <summary>Moves a block one step up or down in the page order by swapping it with its neighbour.</summary>
    public void MoveInOrder(BlockViewModel block, int direction)
    {
        var ordered = Blocks.OrderBy(b => b.PositionY).ThenBy(b => b.PositionX).ToList();
        var index = ordered.IndexOf(block);
        var other = index + direction;
        if (index < 0 || other < 0 || other >= ordered.Count)
            return;

        Structural(direction < 0 ? Loc.T("Moved block up") : Loc.T("Moved block down"), () =>
        {
            var neighbour = ordered[other];
            (block.PositionY, neighbour.PositionY) = (neighbour.PositionY, block.PositionY);
            (block.PositionX, neighbour.PositionX) = (neighbour.PositionX, block.PositionX);
            if (block.PositionY == neighbour.PositionY)
                block.PositionY += direction; // keep the new order even when both were on one row
            return true;
        });
    }

    private BlockViewModel? InsertCopy(BlockModel source)
    {
        if (_currentProject == null)
            return null;

        var copy = source.Clone(); // offsets the position by 20px
        return Structural(Loc.F("Added copy of {0} block", Loc.Block(source.Type)), () =>
        {
            _currentProject.Blocks.Add(copy);
            var vm = AddBlockViewModel(copy);
            SelectedBlock = vm;
            return vm;
        });
    }

    private BlockViewModel AddBlockViewModel(BlockModel model)
    {
        var vm = new BlockViewModel(model);
        vm.Changed += OnBlockChanged;
        Blocks.Add(vm);
        return vm;
    }

    private void RebuildBlocks(List<BlockModel> models, string? selectId)
    {
        SelectedBlock = null;
        foreach (var block in Blocks)
            block.Changed -= OnBlockChanged;
        Blocks.Clear();
        foreach (var model in models.Where(m => string.Equals(m.Page ?? "", CurrentPageSlug, StringComparison.OrdinalIgnoreCase)))
            AddBlockViewModel(model);
        if (selectId != null)
            SelectedBlock = Blocks.FirstOrDefault(b => b.Id == selectId);
        OnPropertyChanged(nameof(CanvasHeight));
    }

    private void OnBlockChanged(object? sender, EventArgs e) => MarkChanged();

    private void MarkChanged()
    {
        ContentChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(CanUndo));
        HasUnsavedChanges = true;
        StatusMessage = Loc.T("Unsaved changes");
        // Debounced: dragging a block fires many changes, the file is written once it settles.
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void RaiseProjectChanged()
    {
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectAuthor));
        OnPropertyChanged(nameof(ProjectTypeName));
        OnPropertyChanged(nameof(ProjectFolder));
        OnPropertyChanged(nameof(DocumentPath));
        OnPropertyChanged(nameof(WorkFolder));
        OnPropertyChanged(nameof(CurrentProject));
        OnPropertyChanged(nameof(CanPaste));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // ========== Undo / redo ==========
    // The history stores snapshots of the block list. Small edits (typing, dragging) are grouped
    // until the editor has been idle for a moment; add/delete/paste each form their own step.

    public void Undo() => StepHistory(_undo, _redo, Loc.T("Undone"));

    public void Redo() => StepHistory(_redo, _undo, Loc.T("Redone"));

    private void StepHistory(Stack<string> from, Stack<string> to, string message)
    {
        if (_currentProject == null) return;
        CommitHistory();
        if (from.Count == 0 || _historyBaseline == null) return;

        to.Push(_historyBaseline);
        _historyBaseline = from.Pop();

        var state = JsonSerializer.Deserialize<HistoryState>(_historyBaseline, GrayCatJson.Options) ?? new HistoryState();
        foreach (var block in state.Blocks)
            PropertyValues.NormalizeForEditing(block.Properties);

        _currentProject.Blocks = state.Blocks;
        _currentProject.Pages = state.Pages;
        ProjectPages.Normalize(_currentProject);
        var keepSlug = CurrentPageSlug;
        ResetPages(_currentProject.Pages.FirstOrDefault(p => p.Slug == keepSlug) ?? _currentProject.Pages[0]);
        RebuildBlocks(state.Blocks, SelectedBlock?.Id);
        MarkChanged();
        StatusMessage = message;
        OnPropertyChanged(nameof(CanRedo));
    }

    private T Structural<T>(string message, Func<T> change)
    {
        CommitHistory();
        var result = change();
        CommitHistory();
        MarkChanged();
        StatusMessage = message;
        return result;
    }

    private void CommitHistory()
    {
        if (_currentProject == null) return;
        var now = Snapshot();
        if (now == _historyBaseline) return;

        if (_historyBaseline != null)
        {
            _undo.Push(_historyBaseline);
            if (_undo.Count > MaxHistory)
            {
                var keep = _undo.Take(MaxHistory).Reverse().ToList();
                _undo.Clear();
                foreach (var item in keep) _undo.Push(item);
            }
        }
        _historyBaseline = now;
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private string Snapshot() => JsonSerializer.Serialize(new HistoryState
    {
        Blocks = _currentProject?.Blocks ?? new List<BlockModel>(),
        Pages = _currentProject?.Pages ?? new List<PageInfo>()
    }, GrayCatJson.Compact);

    private sealed class HistoryState
    {
        public List<BlockModel> Blocks { get; set; } = new();
        public List<PageInfo> Pages { get; set; } = new();
    }

    // ========== Pages ==========

    private void ResetPages(PageInfo current)
    {
        Pages.Clear();
        foreach (var page in _currentProject!.Pages)
            Pages.Add(page);
        _currentPage = current;
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(CurrentPageSlug));
    }

    /// <summary>Adds a page; it starts with copies of the home page's navigation bar and footer.</summary>
    public string? AddPage(string title, string slug)
    {
        if (_currentProject == null) return Loc.T("Open a project first.");
        slug = slug.Trim().ToLowerInvariant();
        if (ProjectPages.ValidateSlug(_currentProject, slug) is { } error)
            return error;

        Structural(Loc.F("Added page {0}", title), () =>
        {
            var page = new PageInfo { Slug = slug, Title = string.IsNullOrWhiteSpace(title) ? slug : title.Trim() };
            _currentProject.Pages.Add(page);

            var y = 20.0;
            foreach (var shared in ProjectPages.BlocksOf(_currentProject, "").Where(b => b.Type is "Navbar" or "Footer")
                         .OrderBy(b => b.Type == "Footer").ThenBy(b => b.PositionY).ToList())
            {
                var copy = shared.Clone();
                copy.Page = slug;
                copy.PositionX = 40;
                copy.PositionY = shared.Type == "Footer" ? 180 : y;
                _currentProject.Blocks.Add(copy);
                y += 130;
            }
            ResetPages(page);
            RebuildBlocks(_currentProject.Blocks, null);
            return page;
        });
        ContentChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>Renames a page and/or changes its address; blocks follow the page.</summary>
    public string? UpdatePage(PageInfo page, string title, string slug, bool showInMenu)
    {
        if (_currentProject == null) return Loc.T("Open a project first.");
        slug = slug.Trim().ToLowerInvariant();
        var isHome = string.IsNullOrEmpty(page.Slug);
        if (!isHome && slug != page.Slug && ProjectPages.ValidateSlug(_currentProject, slug, page.Slug) is { } error)
            return error;

        Structural(Loc.F("Updated page {0}", title), () =>
        {
            if (!isHome && slug != page.Slug)
            {
                foreach (var block in ProjectPages.BlocksOf(_currentProject, page.Slug).ToList())
                    block.Page = slug;
                page.Slug = slug;
            }
            page.Title = string.IsNullOrWhiteSpace(title) ? page.Title : title.Trim();
            page.ShowInMenu = showInMenu;
            ResetPages(_currentPage ?? page);
            return page;
        });
        ContentChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    public void DeletePage(PageInfo page)
    {
        if (_currentProject == null || string.IsNullOrEmpty(page.Slug)) return; // the home page stays
        Structural(Loc.F("Deleted page {0}", page.Title), () =>
        {
            _currentProject.Blocks.RemoveAll(b => string.Equals(b.Page, page.Slug, StringComparison.OrdinalIgnoreCase));
            _currentProject.Pages.Remove(page);
            ResetPages(ReferenceEquals(_currentPage, page) ? _currentProject.Pages[0] : _currentPage!);
            RebuildBlocks(_currentProject.Blocks, null);
            return true;
        });
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MovePage(PageInfo page, int direction)
    {
        if (_currentProject == null) return;
        var list = _currentProject.Pages;
        var index = list.IndexOf(page);
        var target = index + direction;
        if (index <= 0 || target <= 0 || target >= list.Count) return; // home stays first
        Structural(Loc.T("Reordered pages"), () =>
        {
            (list[index], list[target]) = (list[target], list[index]);
            ResetPages(_currentPage ?? page);
            return true;
        });
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets a theme value (colour or font) in the project settings.</summary>
    public void SetSetting(string key, string? value)
    {
        if (_currentProject == null) return;
        if (string.IsNullOrWhiteSpace(value))
            _currentProject.Settings.Remove(key);
        else
            _currentProject.Settings[key] = value.Trim();
        MarkChanged();
    }

    /// <summary>Zips the project folder without installed packages and build output.</summary>
    public async Task<string?> ExportZipAsync(string zipPath)
    {
        if (ProjectFolder == null) return null;
        var generated = await GenerateCodeAsync();
        if (!generated.Success)
            throw new IOException(generated.Message);
        CommitHistory();
        await SaveProjectAsync();
        await Task.Run(() => ProjectArchive.Create(ProjectFolder, zipPath));
        StatusMessage = Loc.F("Project saved as {0}", zipPath);
        return zipPath;
    }
}
