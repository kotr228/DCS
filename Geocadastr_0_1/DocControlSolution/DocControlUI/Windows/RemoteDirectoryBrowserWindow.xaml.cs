using DocControlService.Client;
using DocControlService.Shared;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DocControlUI.Windows
{
    public partial class RemoteDirectoryBrowserWindow : MetroWindow
    {
        private readonly DocControlServiceClient _client;
        private readonly string _deviceName;
        private List<DirectoryWithAccessModel> _remoteDirectories;
        private DirectoryWithAccessModel _selectedDirectory;
        private ObservableCollection<FileSystemItemViewModel> _fileSystemItems;
        private string _currentPath;

        public RemoteDirectoryBrowserWindow(string deviceName)
        {
            InitializeComponent();
            _client = new DocControlServiceClient();
            _deviceName = deviceName;
            _fileSystemItems = new ObservableCollection<FileSystemItemViewModel>();

            DeviceNameText.Text = $"Пристрій: {_deviceName}";
            Title = $"🌐 Віддалені директорії - {_deviceName}";

            Loaded += RemoteDirectoryBrowserWindow_Loaded;
        }

        private async void RemoteDirectoryBrowserWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadRemoteDirectories();
        }

        #region Directory List Management

        private async Task LoadRemoteDirectories()
        {
            try
            {
                SetStatus("Завантаження shared директорій...");

                _remoteDirectories = await _client.GetRemoteDirectoriesAsync(_deviceName);

                Console.WriteLine($"[RemoteDirectoryBrowser] Отримано {_remoteDirectories.Count} директорій");

                foreach (var dir in _remoteDirectories)
                {
                    dir.SharedStatusText = dir.IsShared ? "✅" : "🔒";
                }

                DirectoriesGrid.ItemsSource = _remoteDirectories;
                DirectoryCountText.Text = $"{_remoteDirectories.Count} директорій";

                SetStatus($"Завантажено {_remoteDirectories.Count} директорій");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка: {ex.Message}");
                SetStatus($"Помилка: {ex.Message}");
                await this.ShowMessageAsync("Помилка підключення",
                    $"Не вдалося завантажити директорії з пристрою '{_deviceName}':\n\n{ex.Message}");
            }
        }

        private void DirectoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DirectoriesGrid.SelectedItem is DirectoryWithAccessModel directory)
            {
                _selectedDirectory = directory;
                ShowDirectoryDetails(directory);
                _ = LoadDirectoryStatistics();
                _ = LoadGitHistory();
            }
            else
            {
                ClearDetails();
            }
        }

        #endregion

        #region Statistics Tab

        private void ShowDirectoryDetails(DirectoryWithAccessModel directory)
        {
            DetailNameText.Text = directory.Name;
            DetailPathText.Text = directory.Browse;

            // Встановлюємо початковий шлях для файлового провідника
            _currentPath = directory.Browse;
            CurrentPathTextBox.Text = _currentPath;
            NavigateUpButton.IsEnabled = false;

            // Показуємо статус
            StatsSharedText.Text = directory.IsShared ? "✅ Відкрито" : "🔒 Закрито";
            StatusIndicator.Fill = directory.IsShared ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) :
                                                        new SolidColorBrush(Color.FromRgb(158, 158, 158));
        }

        private void ClearDetails()
        {
            DetailNameText.Text = "-";
            DetailPathText.Text = "-";
            StatsObjectsText.Text = "0";
            StatsFoldersText.Text = "0";
            StatsFilesText.Text = "0";
            StatsSharedText.Text = "-";
            ObjectsProgressBar.Value = 0;
            FoldersProgressBar.Value = 0;
            FilesProgressBar.Value = 0;
        }

        private async Task LoadDirectoryStatistics()
        {
            if (_selectedDirectory == null) return;

            try
            {
                SetStatus("Завантаження статистики...");

                var stats = await _client.GetRemoteDirectoryStatisticsAsync(_deviceName, _selectedDirectory.Id);

                StatsObjectsText.Text = stats.ObjectsCount.ToString();
                StatsFoldersText.Text = stats.FoldersCount.ToString();
                StatsFilesText.Text = stats.FilesCount.ToString();

                // Оновлюємо прогрес бари
                int maxValue = Math.Max(stats.ObjectsCount, Math.Max(stats.FoldersCount, stats.FilesCount));
                if (maxValue > 0)
                {
                    ObjectsProgressBar.Maximum = maxValue;
                    FoldersProgressBar.Maximum = maxValue;
                    FilesProgressBar.Maximum = maxValue;
                    ObjectsProgressBar.Value = stats.ObjectsCount;
                    FoldersProgressBar.Value = stats.FoldersCount;
                    FilesProgressBar.Value = stats.FilesCount;
                }

                SetStatus("Статистика завантажена");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка завантаження статистики: {ex.Message}");
                SetStatus($"Помилка завантаження статистики: {ex.Message}");
            }
        }

        private async void RefreshStats_Click(object sender, RoutedEventArgs e)
        {
            await LoadDirectoryStatistics();
        }

        private async void ScanRemoteDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                await this.ShowMessageAsync("Помилка", "Оберіть директорію для сканування");
                return;
            }

            try
            {
                SetStatus("Сканування директорії...");

                await _client.RemoteScanDirectoryAsync(_deviceName, _selectedDirectory.Id);

                await this.ShowMessageAsync("Успіх", $"Директорія '{_selectedDirectory.Name}' успішно проск анована");
                await LoadDirectoryStatistics();

                SetStatus("Сканування завершено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка сканування: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося просканувати директорію:\n\n{ex.Message}");
                SetStatus("Помилка сканування");
            }
        }

        #endregion

        #region File Explorer Tab

        private async void NavigateToHome_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null) return;

            _currentPath = _selectedDirectory.Browse;
            CurrentPathTextBox.Text = _currentPath;
            NavigateUpButton.IsEnabled = false;
            await LoadFileSystemItems();
        }

        private async void NavigateUp_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath) || _selectedDirectory == null) return;

            var parentPath = System.IO.Path.GetDirectoryName(_currentPath);
            if (!string.IsNullOrEmpty(parentPath) && parentPath.Length >= _selectedDirectory.Browse.Length)
            {
                _currentPath = parentPath;
                CurrentPathTextBox.Text = _currentPath;
                NavigateUpButton.IsEnabled = _currentPath != _selectedDirectory.Browse;
                await LoadFileSystemItems();
            }
        }

        private async Task LoadFileSystemItems()
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            try
            {
                SetStatus("Завантаження файлів...");

                var items = await _client.GetRemoteDirectoryFileListAsync(_deviceName, _currentPath);

                _fileSystemItems.Clear();
                foreach (var item in items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name))
                {
                    _fileSystemItems.Add(new FileSystemItemViewModel(item));
                }

                FileSystemGrid.ItemsSource = _fileSystemItems;

                SetStatus($"Завантажено {items.Count} елементів");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка завантаження файлів: {ex.Message}");
                SetStatus($"Помилка: {ex.Message}");
            }
        }

        private async void FileSystemGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FileSystemGrid.SelectedItem is FileSystemItemViewModel item && item.IsDirectory)
            {
                _currentPath = item.FullPath;
                CurrentPathTextBox.Text = _currentPath;
                NavigateUpButton.IsEnabled = true;
                await LoadFileSystemItems();
            }
        }

        private void FileSystemGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileSystemGrid.SelectedItem is FileSystemItemViewModel item)
            {
                ShowFileDetails(item);
                RenameButton.IsEnabled = true;
                DeleteButton.IsEnabled = true;
            }
            else
            {
                ClearFileDetails();
                RenameButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
            }
        }

        private void ShowFileDetails(FileSystemItemViewModel item)
        {
            FileDetailNameText.Text = item.Name;
            FileDetailPathText.Text = item.FullPath;
            FileDetailTypeText.Text = item.IsDirectory ? "📁 Папка" : $"📄 Файл ({item.Extension})";
            FileDetailSizeText.Text = item.SizeString;
            FileDetailCreatedText.Text = item.CreatedString;
            FileDetailModifiedText.Text = item.ModifiedString;
        }

        private void ClearFileDetails()
        {
            FileDetailNameText.Text = "-";
            FileDetailPathText.Text = "-";
            FileDetailTypeText.Text = "-";
            FileDetailSizeText.Text = "-";
            FileDetailCreatedText.Text = "-";
            FileDetailModifiedText.Text = "-";
        }

        private async void CreateFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            var folderName = await this.ShowInputAsync("Створити папку", "Введіть назву нової папки:");
            if (string.IsNullOrWhiteSpace(folderName)) return;

            try
            {
                SetStatus("Створення папки...");

                await _client.RemoteCreateFolderAsync(_deviceName, _currentPath, folderName);

                await this.ShowMessageAsync("Успіх", $"Папку '{folderName}' успішно створено");
                await LoadFileSystemItems();

                SetStatus("Папку створено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка створення папки: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося створити папку:\n\n{ex.Message}");
                SetStatus("Помилка створення");
            }
        }

        private async void CreateFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            var fileName = await this.ShowInputAsync("Створити файл", "Введіть назву нового файлу:");
            if (string.IsNullOrWhiteSpace(fileName)) return;

            try
            {
                SetStatus("Створення файлу...");

                await _client.RemoteCreateFileAsync(_deviceName, _currentPath, fileName);

                await this.ShowMessageAsync("Успіх", $"Файл '{fileName}' успішно створено");
                await LoadFileSystemItems();

                SetStatus("Файл створено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка створення файлу: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося створити файл:\n\n{ex.Message}");
                SetStatus("Помилка створення");
            }
        }

        private async void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (FileSystemGrid.SelectedItem is not FileSystemItemViewModel item) return;

            var newName = await this.ShowInputAsync("Перейменувати", "Введіть нову назву:", new MetroDialogSettings
            {
                DefaultText = item.Name
            });

            if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

            try
            {
                SetStatus("Перейменування...");

                await _client.RemoteRenameFileOrFolderAsync(_deviceName, item.FullPath, newName);

                await this.ShowMessageAsync("Успіх", $"'{item.Name}' успішно перейменовано в '{newName}'");
                await LoadFileSystemItems();

                SetStatus("Перейменовано");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка перейменування: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося перейменувати:\n\n{ex.Message}");
                SetStatus("Помилка перейменування");
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (FileSystemGrid.SelectedItem is not FileSystemItemViewModel item) return;

            var result = await this.ShowMessageAsync("Підтвердження видалення",
                $"Ви впевнені, що хочете видалити '{item.Name}'?",
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings { AffirmativeButtonText = "Так", NegativeButtonText = "Ні" });

            if (result != MessageDialogResult.Affirmative) return;

            try
            {
                SetStatus("Видалення...");

                await _client.RemoteDeleteFileOrFolderAsync(_deviceName, item.FullPath, item.IsDirectory, recursive: true);

                await this.ShowMessageAsync("Успіх", $"'{item.Name}' успішно видалено");
                await LoadFileSystemItems();

                SetStatus("Видалено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка видалення: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося видалити:\n\n{ex.Message}");
                SetStatus("Помилка видалення");
            }
        }

        private async void RefreshExplorer_Click(object sender, RoutedEventArgs e)
        {
            await LoadFileSystemItems();
        }

        #endregion

        #region Git Tab

        private async Task LoadGitHistory()
        {
            if (_selectedDirectory == null) return;

            try
            {
                SetStatus("Завантаження git історії...");

                var history = await _client.RemoteGetGitHistoryAsync(_deviceName, _selectedDirectory.Id, 100);

                GitHistoryGrid.ItemsSource = history;

                SetStatus($"Завантажено {history.Count} комітів");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка завантаження git історії: {ex.Message}");
                SetStatus("Помилка завантаження історії");
            }
        }

        private async void GitCommit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                await this.ShowMessageAsync("Помилка", "Оберіть директорію");
                return;
            }

            var message = CommitMessageTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                await this.ShowMessageAsync("Помилка", "Введіть повідомлення коміту");
                return;
            }

            try
            {
                SetStatus("Виконання коміту...");
                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                GitStatusLabel.Text = "Виконується...";

                await _client.RemoteGitCommitAsync(_deviceName, _selectedDirectory.Id, message);

                await this.ShowMessageAsync("Успіх", $"Коміт успішно виконано:\n{message}");
                CommitMessageTextBox.Clear();
                await LoadGitHistory();

                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                GitStatusLabel.Text = "Готово";
                SetStatus("Коміт виконано");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка коміту: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося виконати коміт:\n\n{ex.Message}");
                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                GitStatusLabel.Text = "Помилка";
                SetStatus("Помилка коміту");
            }
        }

        private async void GitShowHistory_Click(object sender, RoutedEventArgs e)
        {
            await LoadGitHistory();
        }

        private async void GitRevert_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                await this.ShowMessageAsync("Помилка", "Оберіть директорію");
                return;
            }

            if (GitHistoryGrid.SelectedItem is not GitCommitHistoryModel commit)
            {
                await this.ShowMessageAsync("Помилка", "Оберіть коміт з історії для відкату");
                return;
            }

            var result = await this.ShowMessageAsync("Підтвердження відкату",
                $"Ви впевнені, що хочете відкотити до коміту:\n\n{commit.Hash}\n{commit.Message}\n\nЦе видалить всі зміни після цього коміту!",
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings { AffirmativeButtonText = "Так, відкотити", NegativeButtonText = "Скасувати" });

            if (result != MessageDialogResult.Affirmative) return;

            try
            {
                SetStatus("Відкат версії...");
                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                GitStatusLabel.Text = "Відкат...";

                await _client.RemoteGitRevertAsync(_deviceName, _selectedDirectory.Id, commit.Hash);

                await this.ShowMessageAsync("Успіх", $"Успішно відкочено до коміту:\n{commit.Hash}");
                await LoadGitHistory();

                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                GitStatusLabel.Text = "Готово";
                SetStatus("Відкат виконано");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка відкату: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося виконати відкат:\n\n{ex.Message}");
                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                GitStatusLabel.Text = "Помилка";
                SetStatus("Помилка відкату");
            }
        }

        private async void GitRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadGitHistory();
        }

        #endregion

        #region Common

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadRemoteDirectories();
            if (_selectedDirectory != null)
            {
                await LoadDirectoryStatistics();
                await LoadGitHistory();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        #endregion
    }

    /// <summary>
    /// ViewModel для відображення файлової системи
    /// </summary>
    public class FileSystemItemViewModel
    {
        private readonly FileSystemItemModel _model;

        public FileSystemItemViewModel(FileSystemItemModel model)
        {
            _model = model;
        }

        public string Name => _model.Name;
        public string FullPath => _model.FullPath;
        public bool IsDirectory => _model.IsDirectory;
        public long Size => _model.Size;
        public DateTime CreatedDate => _model.CreatedDate;
        public DateTime ModifiedDate => _model.ModifiedDate;
        public string Extension => _model.Extension;

        public string Icon => IsDirectory ? "📁" : GetFileIcon(Extension);

        public string SizeString
        {
            get
            {
                if (IsDirectory) return "<DIR>";
                if (Size < 1024) return $"{Size} B";
                if (Size < 1024 * 1024) return $"{Size / 1024.0:F2} KB";
                if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024.0):F2} MB";
                return $"{Size / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
        }

        public string CreatedString => CreatedDate.ToString("dd.MM.yyyy HH:mm");
        public string ModifiedString => ModifiedDate.ToString("dd.MM.yyyy HH:mm");

        private string GetFileIcon(string extension)
        {
            return extension?.ToLower() switch
            {
                ".txt" => "📄",
                ".pdf" => "📕",
                ".doc" or ".docx" => "📘",
                ".xls" or ".xlsx" => "📊",
                ".zip" or ".rar" or ".7z" => "📦",
                ".jpg" or ".jpeg" or ".png" or ".gif" => "🖼️",
                ".mp3" or ".wav" => "🎵",
                ".mp4" or ".avi" => "🎬",
                ".cs" or ".js" or ".py" or ".java" => "💻",
                ".xml" or ".json" => "📋",
                _ => "📄"
            };
        }
    }
}
