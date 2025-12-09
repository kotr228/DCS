using DocControlService.Client;
using DocControlService.Shared;
using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

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

                // Оновлюємо текстові значення
                StatsObjectsText.Text = stats.ObjectsCount.ToString();
                StatsFoldersText.Text = stats.FoldersCount.ToString();
                StatsFilesText.Text = stats.FilesCount.ToString();
                StatsDevicesText.Text = stats.AllowedDevicesCount.ToString();
                StatsSharedText.Text = stats.IsShared ? "✅ Відкрито" : "🔒 Закрито";

                // Оновлюємо легенду
                LegendObjectsText.Text = $"Об'єкти: {stats.ObjectsCount}";
                LegendFoldersText.Text = $"Папки: {stats.FoldersCount}";
                LegendFilesText.Text = $"Файли: {stats.FilesCount}";

                // Малюємо кругову діаграму
                DrawPieChart(stats.ObjectsCount, stats.FoldersCount, stats.FilesCount);

                // Оновлюємо прогрес бари
                UpdateProgressBars(stats);

                // Оновлюємо індикатор статусу
                UpdateStatusIndicator(stats.IsShared);

                SetStatus("Готово");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка завантаження статистики: {ex.Message}");
                ClearStatistics();
            }
        }

        private void DrawPieChart(int objects, int folders, int files)
        {
            PieChartCanvas.Children.Clear();

            int total = objects + folders + files;
            if (total == 0)
            {
                // Показуємо порожню діаграму
                var emptyCircle = new Ellipse
                {
                    Width = 120,
                    Height = 120,
                    Fill = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                    Stroke = new SolidColorBrush(Color.FromRgb(189, 189, 189)),
                    StrokeThickness = 2
                };
                Canvas.SetLeft(emptyCircle, 0);
                Canvas.SetTop(emptyCircle, 0);
                PieChartCanvas.Children.Add(emptyCircle);
                return;
            }

            double centerX = 60;
            double centerY = 60;
            double radius = 58;

            double startAngle = -90; // Початок зверху

            // Об'єкти (синій)
            if (objects > 0)
            {
                double angle = (objects / (double)total) * 360;
                DrawPieSlice(centerX, centerY, radius, startAngle, angle, Color.FromRgb(33, 150, 243));
                startAngle += angle;
            }

            // Папки (зелений)
            if (folders > 0)
            {
                double angle = (folders / (double)total) * 360;
                DrawPieSlice(centerX, centerY, radius, startAngle, angle, Color.FromRgb(76, 175, 80));
                startAngle += angle;
            }

            // Файли (помаранчевий)
            if (files > 0)
            {
                double angle = (files / (double)total) * 360;
                DrawPieSlice(centerX, centerY, radius, startAngle, angle, Color.FromRgb(255, 152, 0));
            }
        }

        private void DrawPieSlice(double centerX, double centerY, double radius, double startAngle, double angle, Color color)
        {
            if (angle >= 360)
            {
                // Повне коло
                var circle = new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Fill = new SolidColorBrush(color),
                    Stroke = Brushes.White,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(circle, centerX - radius);
                Canvas.SetTop(circle, centerY - radius);
                PieChartCanvas.Children.Add(circle);
                return;
            }

            var path = new Path
            {
                Fill = new SolidColorBrush(color),
                Stroke = Brushes.White,
                StrokeThickness = 2
            };

            var figure = new PathFigure { StartPoint = new Point(centerX, centerY) };

            double startRad = startAngle * Math.PI / 180;
            double endRad = (startAngle + angle) * Math.PI / 180;

            Point startPoint = new Point(
                centerX + radius * Math.Cos(startRad),
                centerY + radius * Math.Sin(startRad)
            );

            Point endPoint = new Point(
                centerX + radius * Math.Cos(endRad),
                centerY + radius * Math.Sin(endRad)
            );

            figure.Segments.Add(new LineSegment(startPoint, false));
            figure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = angle > 180
            });
            figure.Segments.Add(new LineSegment(new Point(centerX, centerY), false));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            path.Data = geometry;

            PieChartCanvas.Children.Add(path);
        }

        private void UpdateProgressBars(DirectoryStatisticsModel stats)
        {
            int total = stats.ObjectsCount + stats.FoldersCount + stats.FilesCount;

            if (total == 0)
            {
                ObjectsProgressBar.Value = 0;
                FoldersProgressBar.Value = 0;
                FilesProgressBar.Value = 0;
            }
            else
            {
                ObjectsProgressBar.Value = (stats.ObjectsCount / (double)total) * 100;
                FoldersProgressBar.Value = (stats.FoldersCount / (double)total) * 100;
                FilesProgressBar.Value = (stats.FilesCount / (double)total) * 100;
            }

            DevicesProgressBar.Value = Math.Min(stats.AllowedDevicesCount, 20);
        }

        private void UpdateStatusIndicator(bool isShared)
        {
            if (isShared)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Зелений
                StatsSharedText.Text = "✅ Відкрито";
                StatusBorder.Background = new SolidColorBrush(Color.FromArgb(20, 76, 175, 80));
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Червоний
                StatsSharedText.Text = "🔒 Закрито";
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
            }
        }

        private void ClearStatistics()
        {
            StatsObjectsText.Text = "0";
            StatsFoldersText.Text = "0";
            StatsFilesText.Text = "0";
            StatsDevicesText.Text = "0";
            StatsSharedText.Text = "Невідомо";

            LegendObjectsText.Text = "Об'єкти: 0";
            LegendFoldersText.Text = "Папки: 0";
            LegendFilesText.Text = "Файли: 0";

            PieChartCanvas.Children.Clear();
            ObjectsProgressBar.Value = 0;
            FoldersProgressBar.Value = 0;
            FilesProgressBar.Value = 0;
            DevicesProgressBar.Value = 0;

            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(158, 158, 158));
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
