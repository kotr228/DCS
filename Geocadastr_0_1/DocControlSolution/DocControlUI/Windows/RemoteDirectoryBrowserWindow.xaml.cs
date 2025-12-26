using DocControlService.Client;
using DocControlService.Shared;
using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DocControlUI.Windows
{
    public partial class RemoteDirectoryBrowserWindow : MetroWindow
    {
        private readonly DocControlServiceClient _client;
        private readonly string _deviceName;
        private List<DirectoryWithAccessModel> _remoteDirectories;
        private DirectoryWithAccessModel _selectedDirectory;

        public RemoteDirectoryBrowserWindow(string deviceName)
        {
            InitializeComponent();
            _client = new DocControlServiceClient();
            _deviceName = deviceName;

            DeviceNameText.Text = $"Пристрій: {_deviceName}";
            Title = $"🌐 Віддалені директорії - {_deviceName}";

            Loaded += RemoteDirectoryBrowserWindow_Loaded;
        }

        private async void RemoteDirectoryBrowserWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadRemoteDirectories();
        }

        private async System.Threading.Tasks.Task LoadRemoteDirectories()
        {
            try
            {
                SetStatus($"Завантаження shared директорій з {_deviceName}...");

                // Запит до віддаленого пристрою
                _remoteDirectories = await _client.GetRemoteDirectoriesAsync(_deviceName);

                Console.WriteLine($"[RemoteDirectoryBrowser] Отримано {_remoteDirectories.Count} директорій");

                // Додаємо властивість для відображення статусу
                foreach (var dir in _remoteDirectories)
                {
                    dir.SharedStatusText = dir.IsShared ? "✅ Shared" : "🔒 Private";
                }

                DirectoriesGrid.ItemsSource = _remoteDirectories;
                DirectoryCountText.Text = $"{_remoteDirectories.Count} директорій";

                SetStatus($"Завантажено {_remoteDirectories.Count} shared директорій");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка: {ex.Message}");
                SetStatus($"Помилка: {ex.Message}");
                MessageBox.Show(
                    $"Не вдалося завантажити директорії з пристрою '{_deviceName}':\n\n{ex.Message}",
                    "Помилка підключення",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void DirectoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DirectoriesGrid.SelectedItem is DirectoryWithAccessModel directory)
            {
                _selectedDirectory = directory;
                ShowDirectoryDetails(directory);
            }
            else
            {
                ClearDetails();
            }
        }

        private void ShowDirectoryDetails(DirectoryWithAccessModel directory)
        {
            DetailNameText.Text = directory.Name;
            DetailPathText.Text = directory.Browse;

            // Показуємо статистику якщо є
            StatsSharedText.Text = directory.IsShared ? "✅ Відкрито" : "🔒 Закрито";

            // Можна додати більше статистики якщо вона буде передаватись
            StatsObjectsText.Text = "-";
            StatsFoldersText.Text = "-";
            StatsFilesText.Text = "-";
        }

        private void ClearDetails()
        {
            DetailNameText.Text = "-";
            DetailPathText.Text = "-";
            StatsObjectsText.Text = "-";
            StatsFoldersText.Text = "-";
            StatsFilesText.Text = "-";
            StatsSharedText.Text = "-";
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadRemoteDirectories();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }
    }
}
