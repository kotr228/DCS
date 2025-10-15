using GrayCat.Shared.Models;
using GrayCat.UI.Services;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace GrayCat.UI.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly ApiClient _apiClient;
        private ProjectModel? _currentProject;
        private string? _currentProjectPath;

        private bool _isProjectOpen;
        public bool IsProjectOpen
        {
            get => _isProjectOpen;
            set => SetProperty(ref _isProjectOpen, value);
        }

        public ObservableCollection<BlockViewModel> Blocks { get; } = new ObservableCollection<BlockViewModel>();

        public MainViewModel()
        {
            _apiClient = new ApiClient();
        }

        public async Task CreateNewProjectAsync(ProjectGenerationRequest request)
        {
            var result = await _apiClient.GenerateProjectAsync(request);

            if (result.Success && !string.IsNullOrEmpty(result.ProjectPath))
            {
                // Після успішного створення одразу завантажуємо цей проєкт
                LoadProject(Path.Combine(result.ProjectPath, "project.json"));
            }
            else
            {
                System.Windows.MessageBox.Show(result.Message, "Error Creating Project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void LoadProject(string projectFilePath)
        {
            try
            {
                _currentProjectPath = projectFilePath;
                var json = File.ReadAllText(projectFilePath);
                _currentProject = JsonConvert.DeserializeObject<ProjectModel>(json) ?? new ProjectModel();

                Blocks.Clear();
                foreach (var blockModel in _currentProject.Blocks)
                {
                    var blockVM = new BlockViewModel(blockModel);
                    blockVM.PropertyChanged += OnBlockViewModelPropertyChanged;
                    Blocks.Add(blockVM);
                }

                IsProjectOpen = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to load project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsProjectOpen = false;
            }
        }

        public void SaveProject()
        {
            if (_currentProject == null || _currentProjectPath == null) return;

            try
            {
                var json = JsonConvert.SerializeObject(_currentProject, Formatting.Indented);
                File.WriteAllText(_currentProjectPath, json);
                System.Windows.MessageBox.Show("Project saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to save project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void AddBlock(string blockType, double x, double y)
        {
            if (_currentProject == null) return;

            var newBlockModel = new BlockModel
            {
                Id = Guid.NewGuid().ToString(),
                Type = blockType,
                Content = $"New {blockType}...",
                PositionX = x,
                PositionY = y
            };

            _currentProject.Blocks.Add(newBlockModel);

            var blockVM = new BlockViewModel(newBlockModel);
            blockVM.PropertyChanged += OnBlockViewModelPropertyChanged;
            Blocks.Add(blockVM);

            // Auto-save on change
            SaveProject();
        }

        public void RemoveBlock(BlockViewModel blockToRemove)
        {
            if (_currentProject == null) return;

            _currentProject.Blocks.Remove(blockToRemove.Model);
            Blocks.Remove(blockToRemove);
            blockToRemove.PropertyChanged -= OnBlockViewModelPropertyChanged;

            // Auto-save on change
            SaveProject();
        }

        private void OnBlockViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // This event is triggered when a block's position changes.
            // We can add auto-save logic here if needed in the future.
            // For now, we save explicitly or when adding/removing blocks.
        }
    }
}