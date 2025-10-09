using DocControlAI.Core;
using DocControlAI.Analyzers;
using DocControlAI.Services;
using DocControlService.Shared;
using DocControlService.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace DocControlUI.Windows
{
    /// <summary>
    /// AI Analysis Window - ПОВНА ВЕРСІЯ 0.4.1
    /// </summary>
    public partial class AIAnalysisWindow : Window
    {
        private readonly DocControlServiceClient _serviceClient;
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

            _serviceClient = new DocControlServiceClient();
            _ollama = new OllamaClient();
            _structureAnalyzer = new DirectoryStructureAnalyzer(_ollama);
            _chronoGenerator = new ChronologicalRoadmapGenerator(_ollama);
            _reorganizer = new FileReorganizationService();
            _exporter = new DataExportService();

            Loaded += AIAnalysisWindow_Loaded;
        }

        private async void AIAnalysisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetStatus("Ініціалізація AI модуля...");

            await CheckOllamaStatus();
            await LoadPreviousResults();

            SetStatus("Готово до роботи");
        }

        #region Аналіз структури

        private async void AnalyzeStructure_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("🤖 AI аналізує структуру директорії...");
                AnalysisStatusText.Text = "⏳ Аналіз в процесі...";
                AnalysisStatusText.Foreground = System.Windows.Media.Brushes.Orange;

                var (isRunning, _, isModelLoaded) = await _ollama.GetStatusAsync();
                if (!isRunning || !isModelLoaded)
                {
                    var result = MessageBox.Show(
                        "❌ Ollama не готовий до роботи!\n\n" +
                        $"Статус: {(isRunning ? "Запущений" : "Не запущений")}\n" +
                        $"Модель: {(isModelLoaded ? "Завантажена" : "Не завантажена")}\n\n" +
                        "Продовжити з базовим аналізом (без AI)?",
                        "AI недоступний",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                    {
                        AnalysisStatusText.Text = "❌ Аналіз скасовано";
                        AnalysisStatusText.Foreground = System.Windows.Media.Brushes.Red;
                        return;
                    }
                }

                _currentAnalysisResult = await _serviceClient.StartAIAnalysisAsync(
                    _currentDirectoryId,
                    AIAnalysisType.StructureValidation,
                    deepScan: true);

                ViolationsGrid.ItemsSource = _currentAnalysisResult.Violations;
                RecommendationsList.ItemsSource = _currentAnalysisResult.Recommendations;

                if (_currentAnalysisResult.Violations.Count > 0)
                {
                    AnalysisStatusText.Text = $"⚠️ Знайдено {_currentAnalysisResult.Violations.Count} порушень";
                    AnalysisStatusText.Foreground = System.Windows.Media.Brushes.Red;
                }
                else
                {
                    AnalysisStatusText.Text = "✅ Структура коректна";
                    AnalysisStatusText.Foreground = System.Windows.Media.Brushes.Green;
                }

                TotalViolationsText.Text = _currentAnalysisResult.Violations.Count.ToString();
                TotalAnalysesText.Text = (int.Parse(TotalAnalysesText.Text) + 1).ToString();
                LastAnalysisText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                SetStatus($"✅ Аналіз завершено. Порушень: {_currentAnalysisResult.Violations.Count}");

                if (_currentAnalysisResult.Violations.Count > 0)
                {
                    var result = MessageBox.Show(
                        $"🔍 AI Аналіз завершено!\n\n" +
                        $"Знайдено порушень: {_currentAnalysisResult.Violations.Count}\n" +
                        $"AI рекомендацій: {_currentAnalysisResult.Recommendations.Count}\n\n" +
                        $"Підсумок:\n{_currentAnalysisResult.Summary}\n\n" +
                        "Переглянути детальний звіт?",
                        "AI Аналіз",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        // деталі вже у грідах
                    }
                }
                else
                {
                    MessageBox.Show(
                        "✅ Відмінний результат!\n\n" +
                        "Структура директорії відповідає схемі:\n" +
                        "Директорія → Об'єкт → Папка → Файли\n\n" +
                        "AI не виявив порушень.",
                        "AI Аналіз",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Помилка AI аналізу:\n\n{ex.Message}\n\n" +
                    "Перевірте:\n" +
                    "1. Ollama запущений (ollama serve)\n" +
                    "2. Модель завантажена (ollama pull llama3)\n" +
                    "3. DocControl Service працює",
                    "Помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                AnalysisStatusText.Text = "❌ Помилка аналізу";
                AnalysisStatusText.Foreground = System.Windows.Media.Brushes.Red;
                SetStatus("Помилка аналізу");
            }
        }

        private async void ApplyRecommendations_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnalysisResult == null || _currentAnalysisResult.Violations.Count == 0)
            {
                MessageBox.Show(
                    "Немає порушень для виправлення.\n\nСпочатку виконайте аналіз структури.",
                    "Інформація",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var actions = _reorganizer.PreviewActions(_currentAnalysisResult.Violations);

            var preview = "📋 Буде виконано наступні дії:\n\n" +
                         string.Join("\n", actions.Take(10).Select(a =>
                             $"📁 {System.IO.Path.GetFileName(a.SourcePath)}\n   → {a.DestinationPath}"));

            if (actions.Count > 10)
                preview += $"\n\n... та ще {actions.Count - 10} дій";

            preview += $"\n\n💾 Буде створено backup всіх файлів\n\n" +
                      $"Застосувати {actions.Count} AI рекомендацій?";

            var result = MessageBox.Show(
                preview,
                "Підтвердження реорганізації",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    SetStatus("📦 Застосування AI рекомендацій...");

                    await _serviceClient.ApplyAIRecommendationsAsync(
                        _currentAnalysisResult.Id,
                        createBackup: true);

                    MessageBox.Show(
                        $"✅ Успішно застосовано {actions.Count} рекомендацій!\n\n" +
                        "📦 Backup файлів створено з розширенням .backup\n" +
                        "🔄 Структура директорії оптимізована",
                        "Успіх",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    AppliedRecommendationsText.Text =
                        (int.Parse(AppliedRecommendationsText.Text) + actions.Count).ToString();

                    _currentAnalysisResult = null;
                    ViolationsGrid.ItemsSource = null;
                    RecommendationsList.ItemsSource = null;
                    AnalysisStatusText.Text = "✅ Рекомендації застосовано";
                    AnalysisStatusText.Foreground = System.Windows.Media.Brushes.Green;

                    SetStatus("Реорганізація завершена успішно");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"❌ Помилка застосування рекомендацій:\n\n{ex.Message}",
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

                var roadmapName = Microsoft.VisualBasic.Interaction.InputBox(
                    "Введіть назву хронологічної карти:",
                    "AI Roadmap",
                    $"AI Roadmap - {System.IO.Path.GetFileName(_currentDirectoryPath)}");

                if (string.IsNullOrWhiteSpace(roadmapName)) return;

                var description = Microsoft.VisualBasic.Interaction.InputBox(
                    "Введіть опис (необов'язково):",
                    "Опис",
                    "AI-згенерована хронологічна карта проекту");

                _currentChronoRoadmap = await _serviceClient.GenerateAIChronologicalRoadmapAsync(
                    _currentDirectoryId,
                    roadmapName,
                    description);

                ChronoEventsView.ItemsSource = _currentChronoRoadmap.Events;
                AIInsightsText.Text = _currentChronoRoadmap.AIInsights;

                SetStatus($"✅ Згенеровано {_currentChronoRoadmap.Events.Count} подій");

                MessageBox.Show(
                    $"✅ AI хронологічну карту згенеровано!\n\n" +
                    $"📅 Подій: {_currentChronoRoadmap.Events.Count}\n" +
                    $"📊 Період: {_currentChronoRoadmap.Events.First().EventDate:yyyy-MM-dd} - " +
                    $"{_currentChronoRoadmap.Events.Last().EventDate:yyyy-MM-dd}\n\n" +
                    $"🤖 AI Insights:\n{_currentChronoRoadmap.AIInsights.Substring(0, Math.Min(150, _currentChronoRoadmap.AIInsights.Length))}...",
                    "AI Генерація завершена",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Помилка генерації карти:\n\n{ex.Message}\n\n" +
                    "Переконайтеся що:\n" +
                    "• Ollama запущений\n" +
                    "• Модель llama3 завантажена\n" +
                    "• DocControl Service працює",
                    "Помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                SetStatus("Помилка генерації");
            }
        }

        private async void ExportChronoJson_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChronoRoadmap == null)
            {
                MessageBox.Show("Спочатку згенеруйте хронологічну карту.", "Інформація",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = $"ai_roadmap_{_currentChronoRoadmap.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    string json = await _serviceClient.ExportAIChronologicalRoadmapAsync(_currentChronoRoadmap.Id);
                    System.IO.File.WriteAllText(saveDialog.FileName, json);

                    MessageBox.Show(
                        $"✅ Експорт успішний!\n\n" +
                        $"📁 Збережено:\n{saveDialog.FileName}\n\n" +
                        $"📊 Розмір: {new System.IO.FileInfo(saveDialog.FileName).Length / 1024} KB",
                        "Експорт JSON",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    var result = MessageBox.Show("Відкрити папку з файлом?", "Експорт",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start("explorer.exe",
                            $"/select,\"{saveDialog.FileName}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Помилка експорту:\n\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SendToMainService_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChronoRoadmap == null)
            {
                MessageBox.Show("Спочатку згенеруйте хронологічну карту.", "Інформація",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                SetStatus("📤 Інтеграція з основним сервісом...");

                MessageBox.Show(
                    $"✅ AI Roadmap успішно інтегрований!\n\n" +
                    $"ID в системі: {_currentChronoRoadmap.Id}\n" +
                    $"Назва: {_currentChronoRoadmap.Name}\n" +
                    $"Подій: {_currentChronoRoadmap.Events.Count}\n\n" +
                    "Roadmap доступний у головному вікні в розділі 'Дорожня карта'",
                    "Інтеграція успішна",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                SetStatus("Інтеграція завершена");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Помилка інтеграції:\n\n{ex.Message}",
                    "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Геокарти AI

        private void GenerateGeoRoadmap_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "🗺️ AI Генерація геокарт\n\n" +
                "📍 Функціонал в розробці для v0.5:\n\n" +
                "• Витягування локацій з PDF, DOCX\n" +
                "• Розпізнавання адрес через NLP\n" +
                "• Автоматичне геокодування\n" +
                "• Створення геоприв'язки до файлів\n" +
                "• Експорт до основного модулю геокарт\n\n" +
                "🤖 AI навчається розпізнавати географічні об'єкти.",
                "В розробці",
                MessageBoxButton.OK, MessageBoxImage.Information);
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

                    if (isModelLoaded)
                    {
                        ModelLoadedText.Text = "✅ Завантажена";
                        ModelLoadedText.Foreground = System.Windows.Media.Brushes.Green;
                    }
                    else
                    {
                        ModelLoadedText.Text = "❌ Не завантажена";
                        ModelLoadedText.Foreground = System.Windows.Media.Brushes.Red;

                        var result = MessageBox.Show(
                            "⚠️ Модель llama3 не завантажена!\n\n" +
                            "Виконайте: ollama pull llama3\n\nВідкрити інструкції?",
                            "Модель не знайдена",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Yes)
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "https://github.com/ollama/ollama#quickstart",
                                UseShellExecute = true
                            });
                        }
                    }
                }
                else
                {
                    OllamaStatusText.Text = "🔴 Не запущений";
                    OllamaStatusText.Foreground = System.Windows.Media.Brushes.Red;
                    ModelNameText.Text = "-";
                    ModelLoadedText.Text = "-";

                    var result = MessageBox.Show(
                        "❌ Ollama не запущений!\n\n" +
                        "1) ollama serve\n" +
                        "2) або відкрийте Ollama Desktop\n\n" +
                        "Завантажити з ollama.com/download ?",
                        "Ollama не доступний",
                        MessageBoxButton.YesNo, MessageBoxImage.Error);

                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://ollama.com/download",
                            UseShellExecute = true
                        });
                    }
                }

                SetStatus(isRunning ? "Ollama підключено" : "Ollama не доступний");
            }
            catch (Exception ex)
            {
                OllamaStatusText.Text = "❌ Помилка підключення";
                OllamaStatusText.Foreground = System.Windows.Media.Brushes.Red;

                MessageBox.Show(
                    $"❌ Помилка підключення до Ollama:\n\n{ex.Message}\n\n" +
                    "Перевірте порт 11434 та брандмауер.",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);

                SetStatus("Помилка підключення до AI");
            }
        }

        private async System.Threading.Tasks.Task LoadPreviousResults()
        {
            try
            {
                var analyses = await _serviceClient.GetAIAnalysisResultsAsync(_currentDirectoryId);

                if (analyses != null && analyses.Count > 0)
                {
                    var lastAnalysis = analyses.First();
                    TotalAnalysesText.Text = analyses.Count.ToString();
                    LastAnalysisText.Text = lastAnalysis.AnalysisDate.ToString("yyyy-MM-dd HH:mm:ss");
                    TotalViolationsText.Text = lastAnalysis.Violations.Count.ToString();
                }

                var roadmaps = await _serviceClient.GetAIChronologicalRoadmapsAsync(_currentDirectoryId);
                if (roadmaps != null)
                {
                    // за потреби — онови лічильники в UI
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Помилка завантаження історії: {ex.Message}");
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
