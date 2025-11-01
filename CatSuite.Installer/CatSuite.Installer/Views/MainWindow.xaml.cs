using CatSuite.Installer.Models;
using CatSuite.Installer.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CatSuite.Installer.Views
{
    public partial class MainWindow : Window
    {
        private readonly PackageService _packageService;
        private readonly InstallationService _installationService;
        private List<PackageState> _packageStates;
        private CancellationTokenSource _cancellationTokenSource;

        public MainWindow(PackageService packageService, InstallationService installationService)
        {
            InitializeComponent();

            _packageService = packageService;
            _installationService = installationService;

            _installationService.ProgressChanged += OnInstallationProgressChanged;

            LoadPackages();
        }

        private void LoadPackages()
        {
            try
            {
                _packageStates = _packageService.GetAllPackageStates();
                PackagesListView.ItemsSource = _packageStates;

                // Додаємо обробник для зміни кольорів статусу
                PackagesListView.Loaded += (s, e) => ApplyStatusColors();

                UpdateSelectionInfo();
                UpdateSubtitle();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка завантаження пакетів:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyStatusColors()
        {
            // Застосовуємо кольори до Border елементів на основі статусу
            var presenter = PackagesListView.ItemsPanel as ItemsPanelTemplate;
            // Цей метод буде викликатись при рендерингу
        }

        private void UpdateSubtitle()
        {
            int updatesCount = _packageService.GetAvailableUpdatesCount();
            if (updatesCount > 0)
            {
                SubtitleText.Text = $"⚠️ Доступно {updatesCount} оновлень";
            }
            else
            {
                SubtitleText.Text = "✅ Усі пакети актуальні";
            }
        }

        private void UpdateSelectionInfo()
        {
            var selected = _packageStates.Where(p => p.IsSelected).ToList();

            if (selected.Count == 0)
            {
                SelectionInfoText.Text = "Нічого не вибрано";
                InstallButton.IsEnabled = false;
            }
            else
            {
                int installs = selected.Count(p => p.RecommendedAction == PackageAction.Install);
                int updates = selected.Count(p => p.RecommendedAction == PackageAction.Update);
                int removes = selected.Count(p => p.RecommendedAction == PackageAction.Remove);

                var parts = new List<string>();
                if (installs > 0) parts.Add($"встановити: {installs}");
                if (updates > 0) parts.Add($"оновити: {updates}");
                if (removes > 0) parts.Add($"видалити: {removes}");

                SelectionInfoText.Text = $"Вибрано: {string.Join(", ", parts)}";
                InstallButton.IsEnabled = true;
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedPackages = _packageStates.Where(p => p.IsSelected).ToList();

            if (selectedPackages.Count == 0)
            {
                MessageBox.Show("Виберіть пакети для встановлення.",
                    "Інформація", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Підтвердження
            var message = $"Ви збираєтесь виконати наступні дії:\n\n" +
                         string.Join("\n", selectedPackages.Select(p =>
                             $"• {p.Info.Name}: {p.RecommendedAction}"));

            var result = MessageBox.Show(message, "Підтвердження",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            // Побудова плану
            var tasks = _packageService.BuildInstallationPlan(selectedPackages);

            // Показуємо план
            var planMessage = "План встановлення:\n\n" +
                             string.Join("\n", tasks.Select((t, i) =>
                                 $"{i + 1}. {t.PackageName} v{t.Version} ({t.Action})"));

            MessageBox.Show(planMessage, "План встановлення",
                MessageBoxButton.OK, MessageBoxImage.Information);

            // Виконуємо встановлення
            await ExecuteInstallationAsync(tasks);
        }

        private async Task ExecuteInstallationAsync(List<InstallationTask> tasks)
        {
            _cancellationTokenSource = new CancellationTokenSource();

            // Переключаємо UI в режим встановлення
            InstallButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
            ProgressPanel.Visibility = Visibility.Visible;
            PackagesListView.IsEnabled = false;

            try
            {
                var result = await _installationService.ExecuteInstallationPlanAsync(
                    tasks,
                    _cancellationTokenSource.Token);

                // Показуємо результат
                ShowInstallationResult(result);

                // Оновлюємо список
                LoadPackages();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критична помилка:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Повертаємо UI до нормального стану
                InstallButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Collapsed;
                PackagesListView.IsEnabled = true;
            }
        }

        private void OnInstallationProgressChanged(object sender, InstallationProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressText.Text = progress.StatusMessage;
                ProgressPercentage.Text = $"{progress.OverallProgress:F0}%";
                MainProgressBar.Value = progress.OverallProgress;

                if (progress.IsDownloading && progress.TotalBytes > 0)
                {
                    DetailProgressText.Text = $"Завантажено: {progress.DownloadedBytes / 1024 / 1024:F1} MB / {progress.TotalBytes / 1024 / 1024:F1} MB";
                }
                else
                {
                    DetailProgressText.Text = $"Крок {progress.CurrentStep} з {progress.TotalSteps}";
                }
            });
        }

        private void ShowInstallationResult(InstallationResult result)
        {
            string message = result.Message + "\n\n";

            if (result.InstalledPackages.Count > 0)
            {
                message += "✅ Успішно:\n" +
                          string.Join("\n", result.InstalledPackages.Select(p => $"  • {p}")) + "\n\n";
            }

            if (result.FailedPackages.Count > 0)
            {
                message += "❌ Невдало:\n" +
                          string.Join("\n", result.FailedPackages.Select(p => $"  • {p}")) + "\n\n";
            }

            if (result.Errors.Count > 0)
            {
                message += "Помилки:\n" +
                          string.Join("\n", result.Errors.Take(3).Select(e => $"  • {e}"));
            }

            MessageBox.Show(message,
                result.Success ? "Завершено" : "Завершено з помилками",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadPackages();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Скасувати встановлення?",
                "Підтвердження", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _cancellationTokenSource?.Cancel();
            }
        }

        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var historyWindow = new HistoryWindow(_packageService.GetDatabaseManager());
            historyWindow.Owner = this;
            historyWindow.ShowDialog();
        }
    }
}