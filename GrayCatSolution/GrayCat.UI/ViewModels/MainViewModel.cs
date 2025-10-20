namespace GrayCat.UI.ViewModels
{
using GrayCat.Core.Data;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.UI.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
    public class MainViewModel : BaseViewModel
    {
        private readonly ServiceCommunicator _serviceCommunicator;
        private readonly ProjectManager _projectManager;
        private readonly DatabaseService _databaseService;

        private bool _isProjectOpen;
        private ProjectModel? _currentProject;
        private string? _currentProjectPath;
        private BlockViewModel? _selectedBlock;

        public ObservableCollection<BlockViewModel> Blocks { get; } = new();

        public bool IsProjectOpen
        {
            get => _isProjectOpen;
            set => SetProperty(ref _isProjectOpen, value);
        }

        public BlockViewModel? SelectedBlock
        {
            get => _selectedBlock;
            set
            {
                SetProperty(ref _selectedBlock, value);
                OnPropertyChanged(nameof(IsBlockSelected));
            }
        }

        public bool IsBlockSelected => SelectedBlock != null;

        public string ProjectName => _currentProject?.ProjectName ?? "No Project";
        public string ProjectAuthor => _currentProject?.Author ?? "";

        public MainViewModel()
        {
            _serviceCommunicator = new ServiceCommunicator();
            _projectManager = new ProjectManager();
            _databaseService = new DatabaseService();
        }

        public async Task CreateNewProjectAsync(GenerationRequest request)
        {
            try
            {
                var result = await _serviceCommunicator.GenerateProjectAsync(request);

                if (result.Success && !string.IsNullOrEmpty(result.ProjectPath))
                {
                    var projectFile = Path.Combine(result.ProjectPath, "graycat.project.json");

                    // Save to database
                    if (request.ProjectData != null)
                    {
                        await _databaseService.SaveProjectMetadataAsync(request.ProjectData);
                    }

                    await LoadProjectAsync(projectFile);

                    MessageBox.Show(
                        $"Project created successfully!\n\n" +
                        $"Files: {result.FilesCreated}\n" +
                        $"Directories: {result.DirectoriesCreated}\n" +
                        $"Time: {result.DurationMs}ms",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(result.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create project: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task LoadProjectAsync(string projectFilePath)
        {
            try
            {
                _currentProject = await _projectManager.LoadProjectAsync(projectFilePath);
                _currentProjectPath = projectFilePath;

                Blocks.Clear();

                if (_currentProject?.Blocks != null)
                {
                    foreach (var blockModel in _currentProject.Blocks)
                    {
                        var blockVM = new BlockViewModel(blockModel, _databaseService);
                        blockVM.PropertyChanged += OnBlockPropertyChanged;
                        Blocks.Add(blockVM);
                    }
                }

                IsProjectOpen = true;
                OnPropertyChanged(nameof(ProjectName));
                OnPropertyChanged(nameof(ProjectAuthor));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load project: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                IsProjectOpen = false;
            }
        }

        public async Task SaveProjectAsync()
        {
            if (_currentProject == null || _currentProjectPath == null)
                return;

            try
            {
                _currentProject.ModifiedAt = DateTime.UtcNow;

                // Save to JSON
                await _projectManager.SaveProjectAsync(_currentProject, _currentProjectPath);

                // Save to Database
                await _databaseService.SaveProjectMetadataAsync(_currentProject);

                MessageBox.Show("Project saved successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save project: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void AddBlock(string blockType, double x, double y)
        {
            if (_currentProject == null)
                return;

            var newBlockModel = new BlockModel
            {
                Id = Guid.NewGuid().ToString(),
                Type = blockType,
                Content = $"New {blockType} block",
                PositionX = x,
                PositionY = y,
                Width = 200,
                Height = 100,
                Color = "#000000",
                Background = "#FFFFFF",
                FontSize = 14,
                FontFamily = "Arial",
                TextAlignment = "Left"
            };

            _currentProject.Blocks.Add(newBlockModel);

            var blockVM = new BlockViewModel(newBlockModel, _databaseService);
            blockVM.PropertyChanged += OnBlockPropertyChanged;
            Blocks.Add(blockVM);

            // Save to database immediately
            _ = _databaseService.SaveBlockAsync(newBlockModel);

            AutoSave();
        }

        public async void RemoveBlock(BlockViewModel block)
        {
            if (_currentProject == null)
                return;

            _currentProject.Blocks.Remove(block.Model);
            Blocks.Remove(block);
            block.PropertyChanged -= OnBlockPropertyChanged;

            if (SelectedBlock == block)
            {
                SelectedBlock = null;
            }

            // Delete from database
            await _databaseService.DeleteBlockAsync(block.Id);

            AutoSave();
        }

        public void DuplicateBlock(BlockViewModel block)
        {
            if (_currentProject == null)
                return;

            var duplicateModel = new BlockModel
            {
                Id = Guid.NewGuid().ToString(),
                Type = block.Type,
                Content = block.Content,
                PositionX = block.PositionX + 20,
                PositionY = block.PositionY + 20,
                Width = block.Width,
                Height = block.Height,
                Color = block.Color,
                Background = block.Background,
                FontSize = block.FontSize,
                FontFamily = block.FontFamily,
                TextAlignment = block.TextAlignment,
                ImagePath = block.ImagePath,
                BorderRadius = block.BorderRadius,
                Properties = new Dictionary<string, object>(block.Model.Properties),
                Styles = new Dictionary<string, string>(block.Model.Styles)
            };

            _currentProject.Blocks.Add(duplicateModel);

            var duplicateVM = new BlockViewModel(duplicateModel, _databaseService);
            duplicateVM.PropertyChanged += OnBlockPropertyChanged;
            Blocks.Add(duplicateVM);

            // Save to database
            _ = _databaseService.SaveBlockAsync(duplicateModel);

            AutoSave();
        }

        private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Properties are auto-saved in BlockViewModel
            // Just update the UI
            OnPropertyChanged(nameof(Blocks));
        }

        private async void AutoSave()
        {
            if (_currentProject != null && _currentProjectPath != null)
            {
                try
                {
                    _currentProject.ModifiedAt = DateTime.UtcNow;
                    await _projectManager.SaveProjectAsync(_currentProject, _currentProjectPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Auto-save failed: {ex.Message}");
                }
            }
        }

        // For testing
        public void CreateTestProject()
        {
            _currentProject = new ProjectModel
            {
                ProjectName = "Test Project",
                Author = "Test User"
            };
            IsProjectOpen = true;
        }
    }
}