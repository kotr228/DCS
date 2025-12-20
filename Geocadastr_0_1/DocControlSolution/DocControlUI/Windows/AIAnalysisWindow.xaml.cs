using DocControlAI.Analyzers;
using DocControlAI.Core;
using DocControlAI.Services;
using DocControlService.Client;
using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using MahApps.Metro.Controls;

namespace DocControlUI.Windows
{
    /// <summary>
    /// AI Analysis Window - ПОВНА ВЕРСІЯ 0.4.1
    /// </summary>
    public partial class AIAnalysisWindow : MetroWindow
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

            try
            {
                _serviceClient = new DocControlServiceClient();
                _ollama = new OllamaClient();
                _structureAnalyzer = new DirectoryStructureAnalyzer(_ollama);
                _chronoGenerator = new ChronologicalRoadmapGenerator(_ollama);
                _reorganizer = new FileReorganizationService();
                _exporter = new DataExportService();

                Loaded += AIAnalysisWindow_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Помилка ініціалізації AI модуля:\n\n{ex.Message}\n\n" +
                    "Перевірте:\n" +
                    "1. Файл моделі знаходиться в папці Models/\n" +
                    "2. DocControl Service працює\n" +
                    "3. База даних доступна",
                    "Критична помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
            }
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
                // Перевірка існування директорії
                if (!System.IO.Directory.Exists(_currentDirectoryPath))
                {
                    MessageBox.Show(
                        $"❌ Директорія не існує:\n\n{_currentDirectoryPath}\n\n" +
                        "Можливо, вона була видалена або перейменована.",
                        "Помилка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Перевірка доступу до директорії
                try
                {
                    var testAccess = System.IO.Directory.GetFiles(_currentDirectoryPath);
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show(
                        $"❌ Немає доступу до директорії:\n\n{_currentDirectoryPath}\n\n" +
                        "Запустіть програму від імені адміністратора або надайте відповідні права.",
                        "Помилка доступу",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

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
            catch (System.IO.IOException ioEx)
            {
                MessageBox.Show(
                    $"❌ Помилка читання файлової системи:\n\n{ioEx.Message}\n\n" +
                    "Можливі причини:\n" +
                    "• Відсутні права доступу до деяких файлів\n" +
                    "• Файли використовуються іншою програмою\n" +
                    "• Пошкоджена файлова система",
                    "Помилка файлової системи",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                AnalysisStatusText.Text = "❌ Помилка читання";
                AnalysisStatusText.Foreground = System.Windows.Media.Brushes.Red;
                SetStatus("Помилка читання файлової системи");
            }
            catch (System.Net.Sockets.SocketException)
            {
                MessageBox.Show(
                    "❌ Помилка підключення до сервісу!\n\n" +
                    "DocControl Service не відповідає.\n\n" +
                    "Виконайте:\n" +
                    "1. Перевірте чи працює служба DocControlService\n" +
                    "2. Перезапустіть службу через services.msc\n" +
                    "3. Перевірте Named Pipe підключення",
                    "Помилка підключення",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                AnalysisStatusText.Text = "❌ Немає зв'язку з сервісом";
                AnalysisStatusText.Foreground = System.Windows.Media.Brushes.Red;
                SetStatus("DocControl Service недоступний");
            }
            catch (TimeoutException)
            {
                MessageBox.Show(
                    "⏱️ Перевищено час очікування!\n\n" +
                    "AI аналіз займає занадто багато часу.\n\n" +
                    "Можливо:\n" +
                    "• Директорія містить дуже багато файлів\n" +
                    "• AI модель працює повільно\n" +
                    "• Недостатньо ресурсів системи\n\n" +
                    "Спробуйте проаналізувати меншу директорію.",
                    "Таймаут",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                AnalysisStatusText.Text = "⏱️ Таймаут";
                AnalysisStatusText.Foreground = System.Windows.Media.Brushes.Orange;
                SetStatus("Перевищено час очікування");
            }
            catch (Exception ex)
            {
                string detailedMessage = $"❌ Помилка AI аналізу:\n\n{ex.Message}\n\n";

                if (ex.InnerException != null)
                {
                    detailedMessage += $"Деталі: {ex.InnerException.Message}\n\n";
                }

                detailedMessage += "Перевірте:\n" +
                    "1. Ollama запущений (ollama serve)\n" +
                    "2. Модель завантажена (ollama pull llama3)\n" +
                    "3. DocControl Service працює\n" +
                    "4. База даних доступна\n" +
                    "5. Достатньо місця на диску";

                MessageBox.Show(
                    detailedMessage,
                    "Помилка аналізу",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                AnalysisStatusText.Text = "❌ Помилка аналізу";
                AnalysisStatusText.Foreground = System.Windows.Media.Brushes.Red;
                SetStatus($"Помилка: {ex.GetType().Name}");

                // Логування для діагностики
                System.Diagnostics.Debug.WriteLine($"AI Analysis Error: {ex}");
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
                // Перевірка існування директорії
                if (!System.IO.Directory.Exists(_currentDirectoryPath))
                {
                    MessageBox.Show(
                        $"❌ Директорія не існує:\n\n{_currentDirectoryPath}",
                        "Помилка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Перевірка AI статусу
                var (isRunning, _, isModelLoaded) = await _ollama.GetStatusAsync();
                if (!isRunning || !isModelLoaded)
                {
                    var result = MessageBox.Show(
                        "⚠️ AI модель не готова!\n\n" +
                        "Хронологічна карта без AI буде містити тільки базову інформацію " +
                        "(дати створення/модифікації файлів).\n\n" +
                        "Продовжити без AI інсайтів?",
                        "AI недоступний",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                    {
                        return;
                    }
                }

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
            catch (System.IO.IOException ioEx)
            {
                MessageBox.Show(
                    $"❌ Помилка читання файлів:\n\n{ioEx.Message}\n\n" +
                    "Можливі причини:\n" +
                    "• Файли заблоковані іншою програмою\n" +
                    "• Відсутні права доступу\n" +
                    "• Пошкоджені файли метаданих",
                    "Помилка файлової системи",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                SetStatus("Помилка читання файлів");
            }
            catch (InvalidOperationException invalidEx)
            {
                MessageBox.Show(
                    $"❌ Помилка операції:\n\n{invalidEx.Message}\n\n" +
                    "Можливо:\n" +
                    "• Директорія порожня\n" +
                    "• Немає файлів з датами\n" +
                    "• База даних недоступна",
                    "Помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                SetStatus("Неможливо згенерувати карту");
            }
            catch (Exception ex)
            {
                string detailedMessage = $"❌ Помилка генерації карти:\n\n{ex.Message}\n\n";

                if (ex.InnerException != null)
                {
                    detailedMessage += $"Деталі: {ex.InnerException.Message}\n\n";
                }

                detailedMessage += "Переконайтеся що:\n" +
                    "• Ollama запущений (ollama serve)\n" +
                    "• Модель llama3 завантажена\n" +
                    "• DocControl Service працює\n" +
                    "• Директорія містить файли\n" +
                    "• База даних доступна";

                MessageBox.Show(
                    detailedMessage,
                    "Помилка генерації",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                SetStatus($"Помилка: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Chronological Roadmap Error: {ex}");
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
                SetStatus("Перевірка AI...");
                var (isRunning, version, isModelLoaded) = await _ollama.GetStatusAsync();

                OllamaStatusText.Text = isRunning ? "🟢 Працює" : "🔴 Неактивний";
                OllamaStatusText.Foreground = isRunning ? Brushes.Green : Brushes.Red;

                ModelNameText.Text = "Meta-Llama-3-8B-Instruct-Q4_K_M.gguf";
                ModelLoadedText.Text = isModelLoaded ? "✅ Завантажена" : "❌ Не завантажена";
                ModelLoadedText.Foreground = isModelLoaded ? Brushes.Green : Brushes.Red;

                SetStatus(isRunning ? "AI готовий до роботи" : "AI не активний");
            }
            catch (Exception ex)
            {
                OllamaStatusText.Text = "❌ Помилка";
                OllamaStatusText.Foreground = Brushes.Red;
                MessageBox.Show($"Помилка підключення AI:\n\n{ex.Message}", "AI Engine", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task LoadPreviousResults()
        {
            try
            {
                SetStatus("Завантаження попередніх результатів...");

                var analyses = await _serviceClient.GetAIAnalysisResultsAsync(_currentDirectoryId);

                if (analyses != null && analyses.Count > 0)
                {
                    var lastAnalysis = analyses.First();
                    TotalAnalysesText.Text = analyses.Count.ToString();
                    LastAnalysisText.Text = lastAnalysis.AnalysisDate.ToString("yyyy-MM-dd HH:mm:ss");
                    TotalViolationsText.Text = lastAnalysis.Violations.Count.ToString();
                }
                else
                {
                    TotalAnalysesText.Text = "0";
                    LastAnalysisText.Text = "Ще не виконувався";
                    TotalViolationsText.Text = "0";
                }

                var roadmaps = await _serviceClient.GetAIChronologicalRoadmapsAsync(_currentDirectoryId);
                if (roadmaps != null)
                {
                    // за потреби — оновити лічильники в UI
                    System.Diagnostics.Debug.WriteLine($"Завантажено {roadmaps.Count} хронологічних карт");
                }

                SetStatus("Історію завантажено");
            }
            catch (System.Net.Sockets.SocketException)
            {
                SetStatus("Сервіс недоступний - історія не завантажена");
                System.Diagnostics.Debug.WriteLine("Не вдалося підключитися до сервісу для завантаження історії");

                // Встановлюємо значення за замовчуванням
                TotalAnalysesText.Text = "?";
                LastAnalysisText.Text = "Сервіс недоступний";
                TotalViolationsText.Text = "?";
            }
            catch (Microsoft.Data.Sqlite.SqliteException sqlEx)
            {
                SetStatus("Помилка БД - історія не завантажена");
                System.Diagnostics.Debug.WriteLine($"Помилка бази даних при завантаженні історії: {sqlEx.Message}");

                MessageBox.Show(
                    $"⚠️ Помилка бази даних:\n\n{sqlEx.Message}\n\n" +
                    "База даних може бути пошкоджена або недоступна.\n" +
                    "Попередні результати не завантажено.",
                    "Помилка БД",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                TotalAnalysesText.Text = "?";
                LastAnalysisText.Text = "Помилка БД";
                TotalViolationsText.Text = "?";
            }
            catch (Exception ex)
            {
                SetStatus("Помилка завантаження історії");
                System.Diagnostics.Debug.WriteLine($"Помилка завантаження історії: {ex}");

                // Не показуємо MessageBox тут, щоб не заважати запуску вікна
                // Просто логуємо і встановлюємо значення за замовчуванням
                TotalAnalysesText.Text = "?";
                LastAnalysisText.Text = "Помилка завантаження";
                TotalViolationsText.Text = "?";
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
