using DocControlUI.Windows;
using DocControlService.Client;
using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DocControlUI
{
    public partial class MainWindow : Window
    {
        private readonly DocControlServiceClient _client;
        private List<DirectoryWithAccessModel> _directories;
        private List<DeviceModel> _devices;

        public MainWindow()
        {
            InitializeComponent();
            _client = new DocControlServiceClient();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckServiceAndRefresh();
            RefreshNetworkInterfaces_Click(null, null);
        }

        #region Service Status

        private async System.Threading.Tasks.Task CheckServiceAndRefresh()
        {
            try
            {
                SetStatus("Перевірка з'єднання з сервісом...");

                bool available = await _client.IsServiceAvailableAsync();

                if (!available)
                {
                    StatusText.Text = "❌ Не підключено";
                    MessageBox.Show(
                        "Не вдалося підключитися до сервісу.\n\n" +
                        "Переконайтеся що:\n" +
                        "1. Сервіс запущений\n" +
                        "2. У вас є права адміністратора\n" +
                        "3. Брандмауер не блокує Named Pipes",
                        "Помилка підключення",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                StatusText.Text = "✅ Підключено";
                await RefreshAllData();
            }
            catch (Exception ex)
            {
                ShowError("Помилка підключення", ex.Message);
            }
        }

        private async void RefreshStatus_Click(object sender, RoutedEventArgs e)
        {
            await RefreshServiceStatus();
        }

        private async System.Threading.Tasks.Task RefreshServiceStatus()
        {
            try
            {
                var status = await _client.GetStatusAsync();

                StatusText.Text = status.IsRunning ? "✅ Працює" : "❌ Зупинено";
                StartTimeText.Text = status.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
                TotalDirsText.Text = status.TotalDirectories.ToString();
                SharedDirsText.Text = status.SharedDirectories.ToString();
                DevicesCountText.Text = status.RegisteredDevices.ToString();
                LastCommitText.Text = status.LastCommitTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Ще не було";
                CommitIntervalText.Text = $"{status.CommitIntervalMinutes} хвилин";
                CommitIntervalTextBox.Text = status.CommitIntervalMinutes.ToString();
                UnresolvedErrorsCount.Text = status.UnresolvedErrors.ToString();
            }
            catch (Exception ex)
            {
                ShowError("Помилка оновлення статусу", ex.Message);
            }
        }

        #endregion

        #region Data Refresh

        private async System.Threading.Tasks.Task RefreshAllData()
        {
            await RefreshDirectories();
            await RefreshDevices();
            await RefreshServiceStatus();
            await RefreshSettings();
            await RefreshExternalServices();
            await RefreshGeoRoadmaps();

            // Оновлюємо combobox для roadmap
            RoadmapDirectoryCombo.ItemsSource = _directories;
            SetStatus("Дані оновлено");
        }

        private async System.Threading.Tasks.Task RefreshDirectories()
        {
            try
            {
                _directories = await _client.GetDirectoriesAsync();

                var displayData = _directories.Select(d => new
                {
                    d.Id,
                    d.Name,
                    d.Browse,
                    d.IsShared,
                    AllowedDevicesCount = d.AllowedDevices.Count
                }).ToList();

                DirectoriesGrid.ItemsSource = displayData;
                AccessDirectoryCombo.ItemsSource = _directories;

                // Оновлюємо дані для контролю версій
                await RefreshVersionControl();
            }
            catch (Exception ex)
            {
                ShowError("Помилка завантаження директорій", ex.Message);
            }
        }

        private async System.Threading.Tasks.Task RefreshDevices()
        {
            try
            {
                _devices = await _client.GetDevicesAsync();
                DevicesGrid.ItemsSource = _devices;
                AccessDeviceCombo.ItemsSource = _devices;
            }
            catch (Exception ex)
            {
                ShowError("Помилка завантаження пристроїв", ex.Message);
            }
        }

        #endregion

        #region Directory Operations

        private async void AddDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddDirectoryDialog();
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    SetStatus("Додавання директорії...");
                    await _client.AddDirectoryAsync(dialog.DirectoryName, dialog.DirectoryPath);
                    await RefreshDirectories();
                    SetStatus("Директорію додано успішно");
                    MessageBox.Show("Директорію додано та просканована!", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ShowError("Помилка додавання директорії", ex.Message);
                }
            }
        }

        private async void RenameDirectory_Click(object sender, RoutedEventArgs e)
        {
            var selected = DirectoriesGrid.SelectedItem;
            if (selected == null)
            {
                MessageBox.Show("Виберіть директорію для перейменування", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dirId = (int)selected.GetType().GetProperty("Id").GetValue(selected);
            var currentName = selected.GetType().GetProperty("Name").GetValue(selected).ToString();

            var newName = Microsoft.VisualBasic.Interaction.InputBox(
                "Введіть нову назву директорії:",
                "Перейменування",
                currentName);

            if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
            {
                try
                {
                    SetStatus("Перейменування директорії...");
                    await _client.UpdateDirectoryNameAsync(dirId, newName);
                    await RefreshDirectories();
                    SetStatus("Директорію перейменовано");
                }
                catch (Exception ex)
                {
                    ShowError("Помилка перейменування", ex.Message);
                }
            }
        }

        private async void ScanDirectory_Click(object sender, RoutedEventArgs e)
        {
            var selected = DirectoriesGrid.SelectedItem;
            if (selected == null)
            {
                MessageBox.Show("Виберіть директорію для сканування", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dirId = (int)selected.GetType().GetProperty("Id").GetValue(selected);

            try
            {
                SetStatus("Сканування директорії...");
                await _client.ScanDirectoryAsync(dirId);
                SetStatus("Директорію просканована");
                MessageBox.Show("Директорію просканована успішно!", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError("Помилка сканування", ex.Message);
            }
        }

        private async void RemoveDirectory_Click(object sender, RoutedEventArgs e)
        {
            var selected = DirectoriesGrid.SelectedItem;
            if (selected == null)
            {
                MessageBox.Show("Виберіть директорію для видалення", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dirId = (int)selected.GetType().GetProperty("Id").GetValue(selected);
            var dirName = selected.GetType().GetProperty("Name").GetValue(selected).ToString();

            var result = MessageBox.Show(
                $"Ви впевнені що хочете видалити директорію '{dirName}'?\n\n" +
                "Це закриє мережевий доступ та видалить записи з бази даних.",
                "Підтвердження видалення",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    SetStatus("Видалення директорії...");
                    await _client.RemoveDirectoryAsync(dirId);
                    await RefreshDirectories();
                    SetStatus("Директорію видалено");
                }
                catch (Exception ex)
                {
                    ShowError("Помилка видалення", ex.Message);
                }
            }
        }

        private async void OpenAllShares_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("Відкриття всіх шарів...");
                await _client.OpenAllSharesAsync();
                await RefreshDirectories();
                SetStatus("Всі шари відкрито");
                MessageBox.Show("Всі мережеві шари відкрито!", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError("Помилка відкриття шарів", ex.Message);
            }
        }

        private async void CloseAllShares_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Ви впевнені що хочете закрити всі мережеві шари?",
                "Підтвердження",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    SetStatus("Закриття всіх шарів...");
                    await _client.CloseAllSharesAsync();
                    await RefreshDirectories();
                    SetStatus("Всі шари закрито");
                }
                catch (Exception ex)
                {
                    ShowError("Помилка закриття шарів", ex.Message);
                }
            }
        }

        #endregion

        #region Device Operations

        private async void AddDevice_Click(object sender, RoutedEventArgs e)
        {
            var deviceName = Microsoft.VisualBasic.Interaction.InputBox(
                "Введіть назву пристрою:",
                "Додавання пристрою",
                "");

            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                try
                {
                    SetStatus("Додавання пристрою...");
                    await _client.AddDeviceAsync(deviceName, false);
                    await RefreshDevices();
                    SetStatus("Пристрій додано");
                }
                catch (Exception ex)
                {
                    ShowError("Помилка додавання пристрою", ex.Message);
                }
            }
        }

        private async void RemoveDevice_Click(object sender, RoutedEventArgs e)
        {
            var selected = DevicesGrid.SelectedItem as DeviceModel;
            if (selected == null)
            {
                MessageBox.Show("Виберіть пристрій для видалення", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Видалити пристрій '{selected.Name}'?",
                "Підтвердження",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    SetStatus("Видалення пристрою...");
                    await _client.RemoveDeviceAsync(selected.Id);
                    await RefreshDevices();
                    SetStatus("Пристрій видалено");
                }
                catch (Exception ex)
                {
                    ShowError("Помилка видалення пристрою", ex.Message);
                }
            }
        }

        #endregion

        #region Access Control

        private async void GrantAccess_Click(object sender, RoutedEventArgs e)
        {
            var directory = AccessDirectoryCombo.SelectedItem as DirectoryWithAccessModel;
            var device = AccessDeviceCombo.SelectedItem as DeviceModel;

            if (directory == null || device == null)
            {
                MessageBox.Show("Виберіть директорію та пристрій", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                SetStatus("Надання доступу...");
                await _client.GrantAccessAsync(directory.Id, device.Id);
                await RefreshDirectories();
                SetStatus($"Доступ надано: {device.Name} -> {directory.Name}");
                MessageBox.Show("Доступ надано успішно!", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError("Помилка надання доступу", ex.Message);
            }
        }

        private async void RevokeAccess_Click(object sender, RoutedEventArgs e)
        {
            var directory = AccessDirectoryCombo.SelectedItem as DirectoryWithAccessModel;
            var device = AccessDeviceCombo.SelectedItem as DeviceModel;

            if (directory == null || device == null)
            {
                MessageBox.Show("Виберіть директорію та пристрій", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                SetStatus("Відкликання доступу...");
                await _client.RevokeAccessAsync(directory.Id, device.Id);
                await RefreshDirectories();
                SetStatus($"Доступ відкликано: {device.Name} -> {directory.Name}");
            }
            catch (Exception ex)
            {
                ShowError("Помилка відкликання доступу", ex.Message);
            }
        }

        #endregion

        #region Version Control

        private async void RefreshVersionControl_Click(object sender, RoutedEventArgs e)
        {
            await RefreshVersionControl();
        }

        private async System.Threading.Tasks.Task RefreshVersionControl()
        {
            try
            {
                if (_directories == null) return;

                var versionData = _directories.Select(d => new
                {
                    DirectoryName = d.Name,
                    GitStatus = d.GitStatus ?? "Не ініціалізовано",
                    LastCommit = "Завантаження...",
                    LastCommitTime = ""
                }).ToList();

                VersionControlGrid.ItemsSource = versionData;

                // Завантажуємо лог комітів
                var commitLog = await _client.GetCommitLogAsync(limit: 50);
                CommitLogGrid.ItemsSource = commitLog;
            }
            catch (Exception ex)
            {
                ShowError("Помилка оновлення контролю версій", ex.Message);
            }
        }

        private async void ShowCommitHistory_Click(object sender, RoutedEventArgs e)
        {
            var selected = VersionControlGrid.SelectedItem;
            if (selected == null)
            {
                MessageBox.Show("Виберіть директорію", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dirName = selected.GetType().GetProperty("DirectoryName").GetValue(selected).ToString();
                var directory = _directories.FirstOrDefault(d => d.Name == dirName);

                if (directory != null)
                {
                    var history = await _client.GetGitHistoryAsync(directory.Id);

                    var historyWindow = new GitHistoryWindow(history);
                    historyWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                ShowError("Помилка отримання історії", ex.Message);
            }
        }

        private async void RevertVersion_Click(object sender, RoutedEventArgs e)
        {
            var selected = VersionControlGrid.SelectedItem;
            if (selected == null)
            {
                MessageBox.Show("Виберіть директорію для відкату", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dirName = selected.GetType().GetProperty("DirectoryName").GetValue(selected).ToString();
                var directory = _directories.FirstOrDefault(d => d.Name == dirName);

                if (directory != null)
                {
                    var history = await _client.GetGitHistoryAsync(directory.Id);

                    if (history.Count == 0)
                    {
                        MessageBox.Show("Немає історії комітів для відкату", "Інформація",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var commitHash = Microsoft.VisualBasic.Interaction.InputBox(
                        "Введіть hash коміту для відкату:\n\n" +
                        string.Join("\n", history.Take(5).Select(h => $"{h.Hash.Substring(0, 7)} - {h.Message}")),
                        "Відкат версії",
                        history[0].Hash);

                    if (!string.IsNullOrWhiteSpace(commitHash))
                    {
                        await _client.RevertToCommitAsync(directory.Id, commitHash);
                        MessageBox.Show("Відкат виконано успішно!", "Успіх",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        await RefreshVersionControl();
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError("Помилка відкату версії", ex.Message);
            }
        }

        #endregion

        #region Error Management

        private async void RefreshErrors_Click(object sender, RoutedEventArgs e)
        {
            await RefreshErrors();
        }

        private async System.Threading.Tasks.Task RefreshErrors()
        {
            try
            {
                var errors = await _client.GetErrorLogAsync(onlyUnresolved: false);
                ErrorsGrid.ItemsSource = errors;

                var unresolvedCount = await _client.GetUnresolvedErrorCountAsync();
                UnresolvedErrorsCount.Text = unresolvedCount.ToString();
            }
            catch (Exception ex)
            {
                ShowError("Помилка завантаження логу помилок", ex.Message);
            }
        }

        private async void MarkErrorsResolved_Click(object sender, RoutedEventArgs e)
        {
            var selected = ErrorsGrid.SelectedItem as ErrorLogModel;
            if (selected == null)
            {
                MessageBox.Show("Виберіть помилку для позначення", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await _client.MarkErrorResolvedAsync(selected.Id);
                await RefreshErrors();
                SetStatus("Помилку позначено як вирішену");
            }
            catch (Exception ex)
            {
                ShowError("Помилка", ex.Message);
            }
        }

        private async void ClearResolvedErrors_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Видалити всі вирішені помилки з логу?",
                "Підтвердження",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _client.ClearResolvedErrorsAsync();
                    await RefreshErrors();
                    SetStatus("Вирішені помилки очищено");
                }
                catch (Exception ex)
                {
                    ShowError("Помилка очищення", ex.Message);
                }
            }
        }

        #endregion

        #region Settings

        private async System.Threading.Tasks.Task RefreshSettings()
        {
            try
            {
                var settings = await _client.GetSettingsAsync();
                AutoShareOnAddCheckbox.IsChecked = settings.AutoShareOnAdd;
                EnableUpdateNotificationsCheckbox.IsChecked = settings.EnableUpdateNotifications;
            }
            catch (Exception ex)
            {
                // Ігноруємо помилки завантаження налаштувань при старті
            }
        }

        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = new AppSettings
                {
                    AutoShareOnAdd = AutoShareOnAddCheckbox.IsChecked ?? false,
                    EnableUpdateNotifications = EnableUpdateNotificationsCheckbox.IsChecked ?? true,
                    CommitIntervalMinutes = int.TryParse(CommitIntervalTextBox.Text, out int interval) ? interval : 720
                };

                await _client.SaveSettingsAsync(settings);
                SetStatus("Налаштування збережено");
                MessageBox.Show("Налаштування збережено успішно!", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError("Помилка збереження налаштувань", ex.Message);
            }
        }

        private async void SaveCommitInterval_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(CommitIntervalTextBox.Text, out int minutes) && minutes > 0)
            {
                try
                {
                    SetStatus("Збереження налаштувань...");
                    await _client.SetCommitIntervalAsync(minutes);
                    await RefreshServiceStatus();
                    SetStatus("Налаштування збережено");
                    MessageBox.Show($"Інтервал коміту встановлено: {minutes} хвилин", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ShowError("Помилка збереження налаштувань", ex.Message);
                }
            }
            else
            {
                MessageBox.Show("Введіть коректне число хвилин (більше 0)", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void ForceCommit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("Виконання коміту...");
                await _client.ForceCommitAsync();
                await RefreshServiceStatus();
                await RefreshVersionControl();
                SetStatus("Коміт виконано");
                MessageBox.Show("Git коміт виконано для всіх директорій!", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError("Помилка виконання коміту", ex.Message);
            }
        }

        #endregion

        #region External Resources

        private void OpenDocumentation_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Документація буде доступна в наступній версії", "Інформація",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenExamples_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Приклади будуть доступні в наступній версії", "Інформація",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Grid Events

        private void DirectoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DirectoriesGrid.SelectedItem == null)
            {
                AllowedDevicesList.ItemsSource = null;
                return;
            }

            try
            {
                var dirId = (int)DirectoriesGrid.SelectedItem.GetType().GetProperty("Id").GetValue(DirectoriesGrid.SelectedItem);
                var directory = _directories?.FirstOrDefault(d => d.Id == dirId);

                if (directory != null && directory.AllowedDevices != null)
                {
                    AllowedDevicesList.ItemsSource = directory.AllowedDevices.Select(d => d.Name).ToList();
                }
                else
                {
                    AllowedDevicesList.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                // Ігноруємо помилки при виборі
            }
        }

        #endregion

        #region Roadmap Operations

        private List<RoadmapEvent> _currentRoadmapEvents;

        private void RoadmapDirectoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Очищаємо поточний timeline
            TimelineView.ItemsSource = null;
            _currentRoadmapEvents = null;
        }

        private async void AnalyzeForRoadmap_Click(object sender, RoutedEventArgs e)
        {
            var selectedDir = RoadmapDirectoryCombo.SelectedItem as DirectoryWithAccessModel;
            if (selectedDir == null)
            {
                MessageBox.Show("Виберіть директорію для аналізу", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                SetStatus($"Аналіз директорії {selectedDir.Name}...");
                var events = await _client.AnalyzeDirectoryForRoadmapAsync(selectedDir.Id);

                _currentRoadmapEvents = events;
                TimelineView.ItemsSource = events;

                TotalEventsText.Text = events.Count.ToString();
                if (events.Count > 0)
                {
                    var minDate = events.Min(e => e.EventDate);
                    var maxDate = events.Max(e => e.EventDate);
                    DateRangeText.Text = $"{minDate:dd.MM.yyyy} - {maxDate:dd.MM.yyyy}";
                }

                SetStatus($"Знайдено {events.Count} подій");
                MessageBox.Show($"Аналіз завершено!\n\nЗнайдено {events.Count} подій", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError("Помилка аналізу", ex.Message);
            }
        }

        private async void CreateRoadmap_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRoadmapEvents == null || _currentRoadmapEvents.Count == 0)
            {
                MessageBox.Show("Спочатку проаналізуйте директорію", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedDir = RoadmapDirectoryCombo.SelectedItem as DirectoryWithAccessModel;
            if (selectedDir == null) return;

            var roadmapName = Microsoft.VisualBasic.Interaction.InputBox(
                "Введіть назву дорожньої карти:",
                "Створення roadmap",
                $"Roadmap - {selectedDir.Name}");

            if (string.IsNullOrWhiteSpace(roadmapName)) return;

            var description = Microsoft.VisualBasic.Interaction.InputBox(
                "Введіть опис (необов'язково):",
                "Опис roadmap",
                "");

            try
            {
                SetStatus("Створення дорожньої карти...");
                int roadmapId = await _client.CreateRoadmapAsync(
                    selectedDir.Id,
                    roadmapName,
                    description,
                    _currentRoadmapEvents);

                SetStatus("Дорожню карту створено");
                MessageBox.Show($"Дорожню карту '{roadmapName}' створено успішно!\nID: {roadmapId}", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError("Помилка створення roadmap", ex.Message);
            }
        }

        private async void LoadRoadmap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var roadmaps = await _client.GetRoadmapsAsync();

                if (roadmaps.Count == 0)
                {
                    MessageBox.Show("Немає збережених дорожніх карт", "Інформація",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Показуємо діалог вибору
                var roadmapNames = string.Join("\n", roadmaps.Select((r, i) => $"{i + 1}. {r.Name} ({r.Events.Count} подій)"));
                var input = Microsoft.VisualBasic.Interaction.InputBox(
                    "Введіть номер roadmap для завантаження:\n\n" + roadmapNames,
                    "Завантаження roadmap",
                    "1");

                if (int.TryParse(input, out int index) && index > 0 && index <= roadmaps.Count)
                {
                    var roadmap = roadmaps[index - 1];
                    _currentRoadmapEvents = roadmap.Events;
                    TimelineView.ItemsSource = roadmap.Events;

                    TotalEventsText.Text = roadmap.Events.Count.ToString();
                    if (roadmap.Events.Count > 0)
                    {
                        var minDate = roadmap.Events.Min(e => e.EventDate);
                        var maxDate = roadmap.Events.Max(e => e.EventDate);
                        DateRangeText.Text = $"{minDate:dd.MM.yyyy} - {maxDate:dd.MM.yyyy}";
                    }

                    MessageBox.Show($"Завантажено: {roadmap.Name}", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ShowError("Помилка завантаження", ex.Message);
            }
        }

        private async void ExportRoadmapJson_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRoadmapEvents == null || _currentRoadmapEvents.Count == 0)
            {
                MessageBox.Show("Немає даних для експорту", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var roadmaps = await _client.GetRoadmapsAsync();
                if (roadmaps.Count == 0)
                {
                    MessageBox.Show("Спочатку створіть roadmap", "Увага",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Беремо останню створену
                var roadmap = roadmaps.OrderByDescending(r => r.Id).First();

                var json = await _client.ExportRoadmapAsJsonAsync(roadmap.Id);

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = $"roadmap_{roadmap.Name}_{DateTime.Now:yyyyMMdd}.json"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    System.IO.File.WriteAllText(saveDialog.FileName, json);
                    MessageBox.Show("JSON файл збережено успішно!", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ShowError("Помилка експорту", ex.Message);
            }
        }

        private void ExportRoadmapImage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRoadmapEvents == null || _currentRoadmapEvents.Count == 0)
            {
                MessageBox.Show("Немає даних для експорту", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG files (*.png)|*.png",
                    FileName = $"roadmap_{DateTime.Now:yyyyMMdd}.png"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    // Створюємо скріншот timeline
                    var element = TimelineView;
                    var bounds = System.Windows.Media.VisualTreeHelper.GetDescendantBounds(element);

                    var renderBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)bounds.Width,
                        (int)bounds.Height,
                        96, 96,
                        System.Windows.Media.PixelFormats.Pbgra32);

                    var drawingVisual = new System.Windows.Media.DrawingVisual();
                    using (var context = drawingVisual.RenderOpen())
                    {
                        var visualBrush = new System.Windows.Media.VisualBrush(element);
                        context.DrawRectangle(visualBrush, null, new System.Windows.Rect(new System.Windows.Point(), bounds.Size));
                    }

                    renderBitmap.Render(drawingVisual);

                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(renderBitmap));

                    using (var stream = System.IO.File.Create(saveDialog.FileName))
                    {
                        encoder.Save(stream);
                    }

                    MessageBox.Show("Зображення збережено успішно!", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ShowError("Помилка збереження зображення", ex.Message);
            }
        }

        private async void DeleteRoadmap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var roadmaps = await _client.GetRoadmapsAsync();

                if (roadmaps.Count == 0)
                {
                    MessageBox.Show("Немає roadmap для видалення", "Інформація",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var roadmapNames = string.Join("\n", roadmaps.Select((r, i) => $"{i + 1}. {r.Name}"));
                var input = Microsoft.VisualBasic.Interaction.InputBox(
                    "Введіть номер roadmap для видалення:\n\n" + roadmapNames,
                    "Видалення roadmap",
                    "");

                if (int.TryParse(input, out int index) && index > 0 && index <= roadmaps.Count)
                {
                    var roadmap = roadmaps[index - 1];

                    var result = MessageBox.Show(
                        $"Видалити roadmap '{roadmap.Name}'?",
                        "Підтвердження",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await _client.DeleteRoadmapAsync(roadmap.Id);
                        MessageBox.Show("Roadmap видалено", "Успіх",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError("Помилка видалення", ex.Message);
            }
        }

        #endregion

        #region Network Discovery Operations

        private async void ScanNetwork_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ScanStatusText.Text = "Сканування мережі... Це може зайняти 1-2 хвилини...";
                SetStatus("Сканування мережі...");

                var devices = await _client.ScanNetworkAsync();
                NetworkDevicesGrid.ItemsSource = devices;

                ScanStatusText.Text = $"Знайдено {devices.Count} пристроїв";
                SetStatus($"Сканування завершено: {devices.Count} пристроїв");

                MessageBox.Show($"Сканування завершено!\n\nЗнайдено {devices.Count} пристроїв у мережі", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ScanStatusText.Text = "Помилка сканування";
                ShowError("Помилка сканування мережі", ex.Message);
            }
        }

        private async void RefreshNetworkInterfaces_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("Завантаження мережевих інтерфейсів...");
                var interfaces = await _client.GetNetworkInterfacesAsync();
                NetworkInterfacesGrid.ItemsSource = interfaces;
                SetStatus($"Знайдено {interfaces.Count} мережевих інтерфейсів");
            }
            catch (Exception ex)
            {
                ShowError("Помилка завантаження інтерфейсів", ex.Message);
            }
        }

        #endregion

        #region External Services Operations

        private async System.Threading.Tasks.Task RefreshExternalServices()
        {
            try
            {
                var services = await _client.GetExternalServicesAsync();
                ExternalServicesGrid.ItemsSource = services;
            }
            catch (Exception ex)
            {
                ShowError("Помилка завантаження сервісів", ex.Message);
            }
        }

        private async void AddExternalService_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ExternalServiceDialog();
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await _client.AddExternalServiceAsync(
                        dialog.ServiceName,
                        dialog.ServiceType,
                        dialog.ServiceUrl,
                        dialog.ApiKey);

                    await RefreshExternalServices();
                    MessageBox.Show("Сервіс додано успішно!", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ShowError("Помилка додавання сервісу", ex.Message);
                }
            }
        }

        private async void EditExternalService_Click(object sender, RoutedEventArgs e)
        {
            var selected = ExternalServicesGrid.SelectedItem as ExternalService;
            if (selected == null)
            {
                MessageBox.Show("Виберіть сервіс для редагування", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new ExternalServiceDialog(selected);
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    selected.Name = dialog.ServiceName;
                    selected.ServiceType = dialog.ServiceType;
                    selected.Url = dialog.ServiceUrl;
                    selected.ApiKey = dialog.ApiKey;

                    await _client.UpdateExternalServiceAsync(selected);
                    await RefreshExternalServices();

                    MessageBox.Show("Сервіс оновлено!", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ShowError("Помилка оновлення сервісу", ex.Message);
                }
            }
        }

        private async void DeleteExternalService_Click(object sender, RoutedEventArgs e)
        {
            var selected = ExternalServicesGrid.SelectedItem as ExternalService;
            if (selected == null)
            {
                MessageBox.Show("Виберіть сервіс для видалення", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Видалити сервіс '{selected.Name}'?",
                "Підтвердження",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _client.DeleteExternalServiceAsync(selected.Id);
                    await RefreshExternalServices();
                    MessageBox.Show("Сервіс видалено", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ShowError("Помилка видалення", ex.Message);
                }
            }
        }

        private async void TestExternalService_Click(object sender, RoutedEventArgs e)
        {
            var selected = ExternalServicesGrid.SelectedItem as ExternalService;
            if (selected == null)
            {
                MessageBox.Show("Виберіть сервіс для тестування", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                SetStatus($"Тестування з'єднання з {selected.Name}...");
                bool success = await _client.TestExternalServiceAsync(selected.Id);

                if (success)
                {
                    MessageBox.Show($"З'єднання з '{selected.Name}' успішне!", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Не вдалося з'єднатися з '{selected.Name}'", "Помилка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }

                await RefreshExternalServices();
            }
            catch (Exception ex)
            {
                ShowError("Помилка тестування", ex.Message);
            }
        }

        #endregion

        #region Helper Methods

        private void SetStatus(string message)
        {
            StatusBarText.Text = $"{DateTime.Now:HH:mm:ss} - {message}";
        }

        private void ShowError(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"Помилка: {title}");
        }

        #endregion

        #region Geo Roadmaps Operations (v0.3)

        private async void CreateGeoRoadmap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var editorWindow = new GeoRoadmapEditorWindow(_client, null);
                editorWindow.Owner = this;
                editorWindow.ShowDialog();

                await RefreshGeoRoadmaps();
            }
            catch (Exception ex)
            {
                ShowError("Помилка створення геокарти", ex.Message);
            }
        }

        private async void EditGeoRoadmap_Click(object sender, RoutedEventArgs e)
        {
            var selected = GeoRoadmapsGrid.SelectedItem as GeoRoadmap;
            if (selected == null)
            {
                MessageBox.Show("Виберіть геокарту для редагування", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var editorWindow = new GeoRoadmapEditorWindow(_client, selected.Id);
                editorWindow.Owner = this;
                editorWindow.ShowDialog();

                await RefreshGeoRoadmaps();
            }
            catch (Exception ex)
            {
                ShowError("Помилка відкриття редактора", ex.Message);
            }
        }

        private async void DeleteGeoRoadmap_Click(object sender, RoutedEventArgs e)
        {
            var selected = GeoRoadmapsGrid.SelectedItem as GeoRoadmap;
            if (selected == null)
            {
                MessageBox.Show("Виберіть геокарту для видалення", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Видалити геокарту '{selected.Name}'?\n\nБудуть видалені всі пов'язані точки та маршрути.",
                "Підтвердження",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    SetStatus("Видалення геокарти...");
                    await _client.DeleteGeoRoadmapAsync(selected.Id);
                    await RefreshGeoRoadmaps();
                    SetStatus("Геокарту видалено");
                }
                catch (Exception ex)
                {
                    ShowError("Помилка видалення", ex.Message);
                }
            }
        }

        private async void RefreshGeoRoadmaps_Click(object sender, RoutedEventArgs e)
        {
            await RefreshGeoRoadmaps();
        }

        private async System.Threading.Tasks.Task RefreshGeoRoadmaps()
        {
            try
            {
                SetStatus("Завантаження геокарт...");
                var roadmaps = await _client.GetGeoRoadmapsAsync();
                GeoRoadmapsGrid.ItemsSource = roadmaps;
                SetStatus($"Завантажено {roadmaps.Count} геокарт");
            }
            catch (Exception ex)
            {
                ShowError("Помилка завантаження геокарт", ex.Message);
            }
        }

        #endregion
    }

    /// <summary>
    /// Діалог для додавання нової директорії
    /// </summary>
    public class AddDirectoryDialog : Window
    {
        private TextBox nameTextBox;
        private TextBox pathTextBox;

        public string DirectoryName => nameTextBox.Text;
        public string DirectoryPath => pathTextBox.Text;

        public AddDirectoryDialog()
        {
            Title = "Додати директорію";
            Width = 500;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var nameLabel = new TextBlock { Text = "Назва:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(nameLabel, 0);
            grid.Children.Add(nameLabel);

            nameTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(nameTextBox, 0);
            grid.Children.Add(nameTextBox);

            var pathLabel = new TextBlock { Text = "Шлях:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(pathLabel, 1);
            grid.Children.Add(pathLabel);

            var pathPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(pathPanel, 1);

            var browseButton = new Button { Content = "Огляд...", Width = 80, Margin = new Thickness(5, 0, 0, 0) };
            browseButton.Click += (s, e) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Оберіть директорію",
                    CheckFileExists = false,
                    CheckPathExists = true,
                    FileName = "Folder Selection"
                };

                if (dialog.ShowDialog() == true)
                {
                    string selectedPath = System.IO.Path.GetDirectoryName(dialog.FileName);
                    if (!string.IsNullOrEmpty(selectedPath))
                    {
                        pathTextBox.Text = selectedPath;
                    }
                }
            };
            DockPanel.SetDock(browseButton, Dock.Right);
            pathPanel.Children.Add(browseButton);

            pathTextBox = new TextBox();
            pathPanel.Children.Add(pathTextBox);
            grid.Children.Add(pathPanel);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(buttonPanel, 3);

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Margin = new Thickness(5),
                IsDefault = true
            };
            okButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(nameTextBox.Text) ||
                    string.IsNullOrWhiteSpace(pathTextBox.Text))
                {
                    MessageBox.Show("Заповніть всі поля", "Увага",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
                Close();
            };

            var cancelButton = new Button
            {
                Content = "Скасувати",
                Width = 80,
                Margin = new Thickness(5),
                IsCancel = true
            };
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }

    /// <summary>
    /// Вікно для перегляду історії Git комітів
    /// </summary>
    public class GitHistoryWindow : Window
    {
        public GitHistoryWindow(List<GitCommitHistoryModel> history)
        {
            Title = "Історія Git комітів";
            Width = 700;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                ItemsSource = history
            };

            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Hash",
                Binding = new System.Windows.Data.Binding("Hash") { StringFormat = "{0:x7}" },
                Width = 80
            });
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Дата",
                Binding = new System.Windows.Data.Binding("Date") { StringFormat = "yyyy-MM-dd HH:mm" },
                Width = 150
            });
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Автор",
                Binding = new System.Windows.Data.Binding("Author"),
                Width = 150
            });
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Повідомлення",
                Binding = new System.Windows.Data.Binding("Message"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });

            Grid.SetRow(dataGrid, 0);
            grid.Children.Add(dataGrid);

            var closeButton = new Button
            {
                Content = "Закрити",
                Width = 100,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeButton.Click += (s, e) => Close();
            Grid.SetRow(closeButton, 1);
            grid.Children.Add(closeButton);

            Content = grid;
        }
    }
}