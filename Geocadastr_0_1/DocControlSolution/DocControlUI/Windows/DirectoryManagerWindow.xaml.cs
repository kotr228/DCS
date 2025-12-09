using DocControlService.Client;
using DocControlService.Models;
using DocControlService.Shared;
using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DocControlUI.Windows
{
    public partial class DirectoryManagerWindow : MetroWindow
    {
        private readonly DocControlServiceClient _client;
        private List<DirectoryModel> _allDirectories;
        private List<DirectoryModel> _filteredDirectories;
        private DirectoryModel _selectedDirectory;
        private int _selectedDirectoryId;

        public DirectoryManagerWindow()
        {
            InitializeComponent();
            _client = new DocControlServiceClient();
            Loaded += DirectoryManagerWindow_Loaded;
        }

        private async void DirectoryManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshDirectories();
        }

        private async System.Threading.Tasks.Task RefreshDirectories()
        {
            try
            {
                SetStatus("Завантаження директорій...");

                // Отримуємо всі директорії через новий метод або існуючий
                var directoriesWithAccess = await _client.GetDirectoriesAsync();

                // Конвертуємо у простішу модель для відображення
                _allDirectories = directoriesWithAccess.Select(d => new DirectoryModel
                {
                    Id = d.Id,
                    Name = d.Name,
                    Browse = d.Browse
                }).ToList();

                _filteredDirectories = _allDirectories;
                DirectoriesGrid.ItemsSource = _filteredDirectories;
                ResultsCountText.Text = $"Знайдено директорій: {_filteredDirectories.Count}";

                SetStatus("Готово");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка: {ex.Message}");
                MessageBox.Show($"Не вдалося завантажити директорії:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearch();
        }

        private async void SearchTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await PerformSearch();
            }
        }

        private async System.Threading.Tasks.Task PerformSearch()
        {
            try
            {
                string query = SearchTextBox.Text?.Trim();

                if (string.IsNullOrEmpty(query))
                {
                    // Показуємо всі директорії
                    _filteredDirectories = _allDirectories;
                    DirectoriesGrid.ItemsSource = _filteredDirectories;
                    ResultsCountText.Text = $"Знайдено директорій: {_filteredDirectories.Count}";
                    SetStatus("Показано всі директорії");
                    return;
                }

                SetStatus("Пошук...");

                // Використовуємо новий метод пошуку
                var results = await _client.SearchDirectoriesAsync(query);

                _filteredDirectories = results;
                DirectoriesGrid.ItemsSource = _filteredDirectories;
                ResultsCountText.Text = $"Знайдено директорій: {_filteredDirectories.Count}";

                SetStatus($"Знайдено {results.Count} результатів");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка пошуку: {ex.Message}");
                MessageBox.Show($"Помилка при пошуку:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DirectoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DirectoriesGrid.SelectedItem is DirectoryModel directory)
            {
                _selectedDirectory = directory;
                _selectedDirectoryId = directory.Id;

                // Заповнюємо деталі
                DetailIdText.Text = directory.Id.ToString();
                DetailNameTextBox.Text = directory.Name;
                DetailPathTextBox.Text = directory.Browse;

                // Завантажуємо статистику
                await LoadStatistics(directory.Id);
            }
        }

        private async System.Threading.Tasks.Task LoadStatistics(int directoryId)
        {
            try
            {
                SetStatus("Завантаження статистики...");

                var stats = await _client.GetDirectoryStatisticsAsync(directoryId);

                StatsObjectsText.Text = stats.ObjectsCount.ToString();
                StatsFoldersText.Text = stats.FoldersCount.ToString();
                StatsFilesText.Text = stats.FilesCount.ToString();
                StatsDevicesText.Text = stats.AllowedDevicesCount.ToString();
                StatsSharedText.Text = stats.IsShared ? "✅ Відкрито" : "🔒 Закрито";

                SetStatus("Готово");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка завантаження статистики: {ex.Message}");
                ClearStatistics();
            }
        }

        private void ClearStatistics()
        {
            StatsObjectsText.Text = "-";
            StatsFoldersText.Text = "-";
            StatsFilesText.Text = "-";
            StatsDevicesText.Text = "-";
            StatsSharedText.Text = "-";
        }

        private async void SaveChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                MessageBox.Show("Виберіть директорію для редагування",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string newName = DetailNameTextBox.Text?.Trim();
                string newPath = DetailPathTextBox.Text?.Trim();

                if (string.IsNullOrEmpty(newName) || string.IsNullOrEmpty(newPath))
                {
                    MessageBox.Show("Назва та шлях не можуть бути порожніми",
                        "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SetStatus("Збереження змін...");

                await _client.UpdateDirectoryAsync(_selectedDirectoryId, newName, newPath);

                MessageBox.Show("Зміни успішно збережено!",
                    "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                await RefreshDirectories();
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка: {ex.Message}");
                MessageBox.Show($"Не вдалося зберегти зміни:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory != null)
            {
                // Відновлюємо оригінальні значення
                DetailNameTextBox.Text = _selectedDirectory.Name;
                DetailPathTextBox.Text = _selectedDirectory.Browse;
                SetStatus("Зміни скасовано");
            }
        }

        private async void ScanDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                MessageBox.Show("Виберіть директорію для сканування",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SetStatus("Сканування директорії...");

                await _client.ScanDirectoryAsync(_selectedDirectoryId);

                MessageBox.Show("Сканування завершено!",
                    "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                await LoadStatistics(_selectedDirectoryId);
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка: {ex.Message}");
                MessageBox.Show($"Не вдалося відсканувати директорію:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                MessageBox.Show("Виберіть директорію для видалення",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Ви впевнені, що хочете видалити директорію '{_selectedDirectory.Name}'?",
                "Підтвердження", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                SetStatus("Видалення директорії...");

                await _client.RemoveDirectoryAsync(_selectedDirectoryId);

                MessageBox.Show("Директорію успішно видалено!",
                    "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                await RefreshDirectories();
                ClearDetails();
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка: {ex.Message}");
                MessageBox.Show($"Не вдалося видалити директорію:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearDetails()
        {
            DetailIdText.Text = "-";
            DetailNameTextBox.Text = "";
            DetailPathTextBox.Text = "";
            ClearStatistics();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = "";
            await RefreshDirectories();
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }
    }
}
