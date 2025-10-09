using DocControlAI.Core;
using DocControlAI.Analyzers;
using DocControlAI.Services;
using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DocControlUI.Windows
{
    public partial class AIAnalysisWindow : Window
    {
        private readonly OllamaClient _ollama;
        private readonly DirectoryStructureAnalyzer _structureAnalyzer;
        private readonly ChronologicalRoadmapGenerator _chronoGenerator;
        private readonly FileReorganizationService _reorganizer;
        private readonly DataExportService _exporter;

        private string _currentDirectoryPath;
        private int _currentDirectoryId;
        private AIAnalysisResult _currentAnalysisResult;
        private AIChronologicalRoadmap _currentChronoRoadmap;

        public AIAnalysisWindow(string directoryPath, int directoryId)
        {
            InitializeComponent();

            _currentDirectoryPath = directoryPath;
            _currentDirectoryId = directoryId;

            // Ініціалізація AI компонентів
            _ollama = new OllamaClient();
            _structureAnalyzer = new DirectoryStructureAnalyzer(_ollama);
            _chronoGenerator = new ChronologicalRoadmapGenerator(_ollama);
            _reorganizer = new FileReorganizationService();
            _exporter = new DataExportService();

            Loaded += AIAnalysisWindow_Loaded;
        }

        private async void AIAnalysisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetStatus("Перевірка AI статусу...");
            await CheckOllamaStatus();
            SetStatus("Готово");
        }

        #region Аналіз структури

        private async void AnalyzeStructure_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("🤖 AI аналізує структуру директорії...");
                AnalysisStatusText.Text = "⏳ Аналіз...";

                // Запуск AI аналізу
                _currentAnalysisResult = await _structureAnalyzer.AnalyzeStructureAsync(
                    _currentDirectoryPath,
                    _currentDirectoryId);

                // Відображення результатів
                ViolationsGrid.ItemsSource = _currentAnalysisResult.Violations;
                RecommendationsList.ItemsSource = _currentAnalysisResult.Recommendations;

                AnalysisStatusText.Text = _currentAnalysisResult.Violations.Count > 0
                    ? $"⚠️ Знайдено {_currentAnalysisResult.Violations.Count} порушень"
                    : "✅ Структура коректна";

                // Оновлення статистики
                TotalViolationsText.Text = _currentAnalysisResult.Violations.Count.ToString();
                TotalAnalysesText.Text = (int.Parse(TotalAnalysesText.Text) + 1).ToString();
                LastAnalysisText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                SetStatus($"Аналіз завершено. Порушень: {_currentAnalysisResult.Violations.Count}");

                if (_currentAnalysisResult.Violations.Count > 0)
                {
                    var result = MessageBox.Show(
                        $"Знайдено {_currentAnalysisResult.Violations.Count} порушень структури.\n\n" +
                        $"AI згенерував {_currentAnalysisResult.Recommendations.Count} рекомендацій.\n\n" +
                        "Переглянути рекомендації?",
                        "AI Аналіз завершено",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Перемикаємося на вкладку рекомендацій (вже відображено)
                    }
                }
                else
                {
                    MessageBox.Show(
                        "✅ Структура директорії відповідає очікуваній схемі!\n\n" +
                        "Директорія → Об'єкт → Папка → Файли",
                        "Відмінний результат!",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Помилка AI аналізу:\n\n{ex.Message}\n\n" +
                    "Переконайтеся що Ollama запущений: ollama serve",
                    "Помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                AnalysisStatusText.Text = "❌ Помилка аналізу";
                SetStatus("Помилка аналізу");
            }
        }

        private async void ApplyRecommendations_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnalysisResult == null || _currentAnalysisResult.Violations.Count == 0)
            {
                MessageBox.Show(
                    "Немає порушень для виправлення.\n\nСпочатку виконайте аналіз.",
                    "Інформація",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Попередній перегляд дій
            var actions = _reorganizer.PreviewActions(_currentAnalysisResult.Violations);

            var preview = string.Join("\n", actions.Take(10).Select(a =>
                $"📁 {System.IO.Path.GetFileName(a.SourcePath)} → {a.DestinationPath}"));

            if (actions.Count > 10)
                preview += $"\n... та ще {actions.Count - 10} файлів";

            var result = MessageBox.Show(
                $"Буде виконано {actions.Count} дій:\n\n{preview}\n\n" +
                "Буде створено backup файлів.\n\n" +
                "Застосувати зміни?",
                "Підтвердження реорганізації",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    SetStatus("📦 Реорганізація файлів...");

                    bool success = _reorganizer.ApplyReorganization(actions, createBackup: true);

                    if (success)
                    {
                        MessageBox.Show(
                            $"✅ Успішно реорганізовано {actions.Count} файлів!\n\n" +
                            "Backup файли збережені з розширенням .backup",
                            "Успіх",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        // Оновлюємо статистику
                        AppliedRecommendationsText.Text =
                            (int.Parse(AppliedRecommendationsText.Text) + actions.Count).ToString();

                        // Очищаємо результати
                        _currentAnalysisResult = null;
                        ViolationsGrid.ItemsSource = null;
                        RecommendationsList.ItemsSource = null;
                        AnalysisStatusText.Text = "✅ Реорганізацію застосовано";
                    }
                    else
                    {
                        MessageBox.Show(
                            "Помилка реорганізації файлів.\n\nПерегляньте лог для деталей.",
                            "Помилка",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }

                    SetStatus("Реорганізація завершена");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Помилка застосування змін:\n\n{ex.Message}",
                        "Помилка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Хронологічні карти

        private async void GenerateChronoRoadmap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("🤖 AI генерує хронологічну карту...");

                // Генерація карти
                _currentChronoRoadmap = await _chronoGenerator.GenerateRoadmapAsync(
                    _currentDirectoryPath,
                    _currentDirectoryId,
                    $"Проект {System.IO.Path.GetFileName(_currentDirectoryPath)}");

                // Відображення подій
                ChronoEventsView.ItemsSource = _currentChronoRoadmap.Events;
                AIInsightsText.Text = _currentChronoRoadmap.AIInsights;

                SetStatus($"Згенеровано {_currentChronoRoadmap.Events.Count} подій");

                MessageBox.Show(
                    $"✅ Хронологічну карту згенеровано!\n\n" +
                    $"Подій: {_currentChronoRoadmap.Events.Count}\n" +
                    $"Період: {_currentChronoRoadmap.Events.First().EventDate:yyyy-MM-dd} - " +
                    $"{_currentChronoRoadmap.Events.Last().EventDate:yyyy-MM-dd}",
                    "AI Генерація завершена",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Помилка генерації карти:\n\n{ex.Message}\n\n" +
                    "Переконайтеся що Ollama запущений.",
                    "Помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                SetStatus("Помилка генерації");
            }
        }

        private void ExportChronoJson_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChronoRoadmap == null)
            {
                MessageBox.Show(
                    "Спочатку згенеруйте хронологічну карту.",
                    "Інформація",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = $"chrono_roadmap_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    string json = _exporter.ExportChronologicalRoadmap(_currentChronoRoadmap);
                    bool success = _exporter.SaveToFile(json, saveDialog.FileName);

                    if (success)
                    {
                        MessageBox.Show(
                            $"✅ Експорт успішний!\n\n{saveDialog.FileName}",
                            "Експорт JSON",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Помилка експорту:\n\n{ex.Message}",
                    "Помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SendToMainService_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChronoRoadmap == null)
            {
                MessageBox.Show(
                    "Спочатку згенеруйте хронологічну карту.",
                    "Інформація",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                // Експорт в JSON
                string json = _exporter.ExportChronologicalRoadmap(_currentChronoRoadmap);

                // TODO: Надіслати до головного сервісу через API
                // Поки що зберігаємо локально

                string tempPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"roadmap_transfer_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                _exporter.SaveToFile(json, tempPath);

                MessageBox.Show(
                    $"📤 Дані підготовлено до передачі!\n\n" +
                    $"Тимчасовий файл:\n{tempPath}\n\n" +
                    "Інтеграція з головним сервісом буде доступна в наступній версії.",
                    "Експорт",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Помилка передачі даних:\n\n{ex.Message}",
                    "Помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Геокарти AI

        private void GenerateGeoRoadmap_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "🗺️ AI Генерація геокарт\n\n" +
                "Функція в розробці:\n" +
                "- Витягування локацій з PDF, DOCX\n" +
                "- Геокодування адрес\n" +
                "- Створення геоприв'язки\n" +
                "- Експорт до основного модулю геокарт\n\n" +
                "Буде доступно в v0.5",
                "В розробці",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        #endregion

        #region AI Статус

        private async void CheckOllama_Click(object sender, RoutedEventArgs e)
        {
            await CheckOllamaStatus();
        }

        private async System.Threading.Tasks.Task CheckOllamaStatus()
        {
            try
            {
                SetStatus("Перевірка Ollama...");

                var (isRunning, version, isModelLoaded) = await _ollama.GetStatusAsync();

                if (isRunning)
                {
                    OllamaStatusText.Text = "🟢 Запущений";
                    OllamaStatusText.Foreground = System.Windows.Media.Brushes.Green;
                    ModelNameText.Text = "llama3";
                    ModelLoadedText.Text = isModelLoaded ? "✅ Так" : "❌ Ні (виконайте: ollama pull llama3)";
                    ModelLoadedText.Foreground = isModelLoaded
                        ? System.Windows.Media.Brushes.Green
                        : System.Windows.Media.Brushes.Red;

                    if (!isModelLoaded)
                    {
                        MessageBox.Show(
                            "⚠️ Модель llama3 не завантажена!\n\n" +
                            "Виконайте в терміналі:\n" +
                            "ollama pull llama3\n\n" +
                            "Це займе ~4GB місця та кілька хвилин.",
                            "Модель не знайдена",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                else
                {
                    OllamaStatusText.Text = "🔴 Не запущений";
                    OllamaStatusText.Foreground = System.Windows.Media.Brushes.Red;
                    ModelNameText.Text = "-";
                    ModelLoadedText.Text = "-";

                    MessageBox.Show(
                        "❌ Ollama не запущений!\n\n" +
                        "Запустіть Ollama:\n" +
                        "1. Відкрийте термінал\n" +
                        "2. Виконайте: ollama serve\n" +
                        "3. Або запустіть Ollama Desktop App\n\n" +
                        "Завантажити: https://ollama.com/download",
                        "Ollama не доступний",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                SetStatus(isRunning ? "Ollama підключено" : "Ollama не доступний");
            }
            catch (Exception ex)
            {
                OllamaStatusText.Text = "❌ Помилка підключення";
                OllamaStatusText.Foreground = System.Windows.Media.Brushes.Red;

                MessageBox.Show(
                    $"Помилка підключення до Ollama:\n\n{ex.Message}\n\n" +
                    "Перевірте чи Ollama запущений: ollama serve",
                    "Помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Helper Methods

        private void SetStatus(string message)
        {
            StatusBarText.Text = $"{DateTime.Now:HH:mm:ss} - {message}";
        }

        #endregion
    }
}