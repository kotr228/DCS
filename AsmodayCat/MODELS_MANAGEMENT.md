# ASMODAYCAT - MODEL POOL MANAGEMENT (PHASE 8)

## 1. Огляд завдання
Вікно `ModelsView` (Model Pool) має стати центром управління життєвим циклом LLM. Наразі там є лише каркас таблиці з колонками: *Model, Task, VRAM, Status, Progress, Action*. 
Необхідно реалізувати логіку збору інформації про встановлені моделі (через локальне API Ollama / llama.cpp), відображення рекомендованих моделей з нашої "Матриці" (які ще не встановлені), та забезпечити можливість їх завантаження (Pull) з відображенням прогресу.

## 2. Функціональні вимоги (FR-M)
*   **FR-M1 (Discovery):** Сервіс повинен опитувати локальний рушій (наприклад, `GET /api/tags` в Ollama) і повертати список встановлених моделей. 
*   **FR-M2 (Matrix Merging):** У таблиці завжди повинні відображатися базові моделі екосистеми (Mistral, Qwen-Coder, Phi3, Llava), навіть якщо вони не встановлені (Status = "Not Installed").
*   **FR-M3 (Downloading):** При натисканні "Pull/Download" для відсутньої моделі, сервіс починає скачування, а в колонку "Progress" у UI транслюється прогрес-бар (0-100%).
*   **FR-M4 (VRAM Management):** Для моделей, які зараз завантажені в пам'ять (Status = "Loaded"), колонка VRAM має показувати використану пам'ять, а в Action має з'явитися кнопка "Unload" (для звільнення ресурсів).
*   **FR-M5 (Custom Models):** Під або над таблицею має бути просте поле для вводу назви будь-якої іншої моделі (наприклад, `deepseek-r1:8b`) та кнопка "Pull Custom", щоб додати її в пул.

---

## 3. ДЕКОМПОЗИЦІЯ ТА ПОКРОКОВІ ТАСКИ (ІНСТРУКЦІЯ ДЛЯ CLAUDE)

**@Claude:** Це інструкція для реалізації логіки управління моделями. Виконуй кроки послідовно.

### Step 1: DTO та Моделі (AsmodayCat.Shared)
1. Створи `LlmModelDto` з властивостями:
   * `string Name` (наприклад, "mistral:7b").
   * `string RecommendedTask` (наприклад, "General Agent").
   * `long VramUsageBytes` (0 якщо не в пам'яті).
   * `ModelPoolStatus Status` (Enum: NotInstalled, Downloading, Ready, Loaded).
   * `double DownloadProgress` (0.0 до 100.0).

### Step 2: Логіка управління (AsmodayCat.Core & AsmodayCat.Service)
1. В `AsmodayCat.Core` реалізуй `OllamaClient` (використовуй стандартний `HttpClient` для запитів до `localhost:11434` або відповідного порту).
   * Метод `GetLocalModelsAsync()` -> парсить JSON-відповідь від Ollama.
   * Метод `PullModelAsync(string modelName)` -> обробляє потокову JSON-відповідь (streaming) для вирахування прогресу.
2. В `AsmodayCat.Service` створи IPC-обробники: 
   * `GetModelPool` (повертає злитий список: локальні + наші рекомендовані).
   * `StartModelPull` (запускає скачування і починає пушити прогрес у UI).
   * `UnloadModel` (відправляє команду на очищення пам'яті).

### Step 3: ViewModel (AsmodayCat.UI)
1. Відкрий/створи `ModelsViewModel`.
2. Додай `ObservableCollection<LlmModelDto> ModelPool`.
3. Реалізуй команди:
   * `RefreshCommand` (викликається при завантаженні вікна та по кнопці).
   * `ActionCommand(LlmModelDto model)`: логіка змінюється залежно від статусу (якщо `NotInstalled` -> відправляє IPC команду на Pull; якщо `Loaded` -> відправляє IPC на Unload; якщо `Ready` -> можливо, тестовий запуск).
   * `PullCustomModelCommand` (зв'язана з новим текстовим полем).

### Step 4: Оновлення XAML (AsmodayCat.UI)
1. Відкрий `ModelsView.xaml`.
2. Прив'яжи поточну таблицю (ListView/DataGrid) до `ModelPool`.
3. Налаштуй `CellTemplates` (або `DataGridTemplateColumn`):
   * **VRAM:** Показувати значення форматовано (наприклад, "4.2 GB") лише якщо статус `Loaded`, інакше — "-".
   * **Status:** Кольоровий `Chip` або текст (Сірий — Not Installed, Жовтий — Downloading, Зелений — Ready, Пурпурний — Loaded).
   * **Progress:** Показувати `ProgressBar` (з MaterialDesignThemes) *тільки* якщо статус `Downloading`.
   * **Action:** Кнопки з іконками (`PackIcon`). Якщо Not Installed — іконка Download. Якщо Loaded — іконка Stop/Unload. Якщо Ready — іконка Play/Test або Trash (видалити).
4. Додай над таблицею (поруч із кнопкою Refresh) `TextBox` з плейсхолдером "custom-model-name:tag" та кнопку "Pull".