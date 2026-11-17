namespace AsmodayCat.UI.Localization;

// All interface texts: key → [Ukrainian, English, Russian].
// Split by screen into partial files (Strings.Chat.cs, …).
public static partial class Strings
{
    public static readonly Dictionary<string, string[]> Table = new(StringComparer.Ordinal);

    private static void A(string key, string uk, string en, string ru) => Table[key] = [uk, en, ru];

    static Strings()
    {
        Common();
        Labels();
        MainWindow();
        Dashboard();
        Chat();
        Models();
        Settings();
        Agents();
        Memory();
        Terminal();
        Startup();
        Dialogs();
        Tray();
        Voice();
    }

    private static void Common()
    {
        A("lang.uk", "Українська", "Українська", "Українська");
        A("lang.en", "English", "English", "English");
        A("lang.ru", "Русский", "Русский", "Русский");
        A("lang.tooltip", "Мова інтерфейсу", "Interface language", "Язык интерфейса");

        A("common.refresh", "Оновити", "Refresh", "Обновить");
        A("common.save", "Зберегти", "Save", "Сохранить");
        A("common.cancel", "Скасувати", "Cancel", "Отмена");
        A("common.delete", "Видалити", "Delete", "Удалить");
        A("common.minimize", "Згорнути", "Minimize", "Свернуть");
        A("common.maximize", "Розгорнути", "Maximize", "Развернуть");
        A("common.restore", "Відновити", "Restore", "Восстановить");
        A("common.hideToTray", "Сховати в трей", "Hide to tray", "Скрыть в трей");
        A("common.exit", "Вийти", "Exit", "Выйти");

        A("common.error", "Помилка: {0}", "Error: {0}", "Ошибка: {0}");
        A("dash.killed", "Усе зупинено, правила агентів на паузі.", "Everything stopped; agent rules are paused.", "Всё остановлено, правила агентов на паузе.");

        A("fmt.mb", "{0:N0} МБ", "{0:N0} MB", "{0:N0} МБ");
        A("fmt.ofMb", "з {0:N0} МБ", "of {0:N0} MB", "из {0:N0} МБ");
        A("fmt.percent", "{0:F1}%", "{0:F1}%", "{0:F1}%");
    }

    private static void Labels()
    {
        // Model pool status
        A("label.NotInstalled", "Не встановлена", "Not installed", "Не установлена");
        A("label.Downloading", "Завантажується", "Downloading", "Загружается");
        A("label.Ready", "Готова", "Ready", "Готова");
        A("label.Loaded", "У пам’яті", "In memory", "В памяти");
        // Model tasks
        A("label.General", "Загальна", "General", "Общая");
        A("label.Coding", "Код", "Code", "Код");
        A("label.FastAnalytics", "Швидкий аналіз", "Quick analysis", "Быстрый анализ");
        A("label.Vision", "Зображення", "Images", "Изображения");
        A("label.Custom", "Інша", "Other", "Другая");
        A("label.Images (SD WebUI)", "Генерація зображень (SD WebUI)", "Image generation (SD WebUI)", "Генерация изображений (SD WebUI)");
        // Agent actions
        A("label.CreateReport", "Звіт", "Report", "Отчёт");
        A("label.Translate", "Переклад", "Translation", "Перевод");
        A("label.Refactor", "Рефакторинг коду", "Code refactoring", "Рефакторинг кода");
        // Engine / service state
        A("label.Idle", "Очікує", "Idle", "Ожидает");
        A("label.Generating", "Генерує відповідь", "Generating", "Генерирует ответ");
        A("label.Running", "Працює", "Running", "Работает");
        A("label.Offline", "Недоступна", "Offline", "Недоступна");
        A("label.Unknown", "Невідомо", "Unknown", "Неизвестно");
        A("label.Unloaded", "Вивантажено", "Unloaded", "Выгружено");
        A("label.Processing", "Обробляється", "Processing", "Обрабатывается");
        A("label.Done", "Готово", "Done", "Готово");
        A("label.Error", "Помилка", "Error", "Ошибка");
        // Devices
        A("label.Auto", "Авто", "Auto", "Авто");
        A("label.GPU", "Відеокарта (GPU)", "Graphics card (GPU)", "Видеокарта (GPU)");
        A("label.CPU", "Процесор (CPU)", "Processor (CPU)", "Процессор (CPU)");
    }

    private static void MainWindow()
    {
        A("main.title", "AsmodayCat — Панель керування", "AsmodayCat — Control panel", "AsmodayCat — Панель управления");
        A("main.tagline", "локальний ШІ-помічник", "local AI assistant", "локальный ИИ-помощник");
        A("main.sections", "РОЗДІЛИ", "SECTIONS", "РАЗДЕЛЫ");
        A("main.version", "Версія {0}", "Version {0}", "Версия {0}");
        A("nav.dashboard", "Огляд", "Overview", "Обзор");
        A("nav.chat", "Чат", "Chat", "Чат");
        A("nav.agents", "Агенти в папках", "Folder agents", "Агенты в папках");
        A("nav.memory", "Пам’ять", "Memory", "Память");
        A("nav.models", "Моделі", "Models", "Модели");
        A("nav.settings", "Налаштування", "Settings", "Настройки");
        A("nav.terminal", "Термінал", "Terminal", "Терминал");
    }
}
