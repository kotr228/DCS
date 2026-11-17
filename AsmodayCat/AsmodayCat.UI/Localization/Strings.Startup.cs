namespace AsmodayCat.UI.Localization;

public static partial class Strings
{
    private static void Startup()
    {
        A("startup.title", "AsmodayCat — Запуск", "AsmodayCat — Starting", "AsmodayCat — Запуск");
        A("startup.tagline", "Локальний ШІ-помічник і агенти для ваших тек",
                             "A local AI assistant and agents for your folders",
                             "Локальный ИИ-помощник и агенты для ваших папок");
        A("startup.checksTitle", "Перевірка перед запуском", "Startup checks", "Проверка перед запуском");
        A("startup.service", "Фонова служба AsmodayCat", "AsmodayCat background service", "Фоновая служба AsmodayCat");
        A("startup.llm", "Мовна модель (Ollama)", "Language model (Ollama)", "Языковая модель (Ollama)");
        A("startup.installOllama", "Встановити Ollama (≈1,7 ГБ)", "Install Ollama (≈1.7 GB)", "Установить Ollama (≈1,7 ГБ)");
        A("startup.installModel", "Встановити рекомендовану модель qwen2.5:7b (≈4,7 ГБ)",
                                  "Install the recommended model qwen2.5:7b (≈4.7 GB)",
                                  "Установить рекомендуемую модель qwen2.5:7b (≈4,7 ГБ)");
        A("startup.continue", "Продовжити", "Continue", "Продолжить");
        A("startup.exit", "Вихід", "Exit", "Выход");
        A("startup.footer", "© 2026 CatSuite | AsmodayCat {0} — локальний ШІ-помічник",
                            "© 2026 CatSuite | AsmodayCat {0} — local AI assistant",
                            "© 2026 CatSuite | AsmodayCat {0} — локальный ИИ-помощник");

        A("startup.starting", "Запуск…", "Starting…", "Запуск…");
        A("startup.checking", "Перевірка…", "Checking…", "Проверка…");
        A("startup.connecting", "Підключення до служби AsmodayCat…", "Connecting to the AsmodayCat service…", "Подключение к службе AsmodayCat…");
        A("startup.startingLlm", "Запуск мовної моделі (Ollama)…", "Starting the language model (Ollama)…", "Запуск языковой модели (Ollama)…");
        A("startup.svcRunning", "Працює", "Running", "Работает");
        A("startup.svcNotRunning", "Не запущена", "Not running", "Не запущена");
        A("startup.unavailable", "Недоступно", "Unavailable", "Недоступно");
        A("startup.svcFailed", "Служба не запустилася — частина функцій буде недоступна.",
                               "The service did not start — some features will be unavailable.",
                               "Служба не запустилась — часть функций будет недоступна.");
        A("startup.llmNotRunning", "Не запущено", "Not running", "Не запущено");
        A("startup.ollamaNotRunning", "Ollama не запущена.", "Ollama is not running.", "Ollama не запущена.");
        A("startup.noModels", "Немає моделей", "No models", "Нет моделей");
        A("startup.noModelsHint", "Ollama працює, але моделей не встановлено — відкрийте вкладку «Моделі».",
                                  "Ollama is running, but no models are installed — open the Models tab.",
                                  "Ollama работает, но модели не установлены — откройте вкладку «Модели».");
        A("startup.ready", "Готово ({0} {1})", "Ready ({0} {1})", "Готово ({0} {1})");
        A("startup.allReady", "Усе готово.", "All set.", "Всё готово.");
        A("plural.model.one", "модель", "model", "модель");
        A("plural.model.few", "моделі", "models", "модели");
        A("plural.model.many", "моделей", "models", "моделей");

        A("startup.installingOllama", "Встановлюю Ollama…", "Installing Ollama…", "Устанавливаю Ollama…");
        A("startup.downloadingOllama", "Завантажую Ollama… {0:F0}% (≈1,7 ГБ)", "Downloading Ollama… {0:F0}% (≈1.7 GB)", "Загружаю Ollama… {0:F0}% (≈1,7 ГБ)");
        A("startup.ollamaInstallFailed", "Не вдалося встановити Ollama: {0}", "Could not install Ollama: {0}", "Не удалось установить Ollama: {0}");
        A("startup.ollamaExitCode", "Інсталятор Ollama завершився з кодом {0}.", "The Ollama installer exited with code {0}.", "Установщик Ollama завершился с кодом {0}.");
        A("startup.downloadingModel", "Завантажую {0}… {1:F0}% (≈4,7 ГБ)", "Downloading {0}… {1:F0}% (≈4.7 GB)", "Загружаю {0}… {1:F0}% (≈4,7 ГБ)");
        A("startup.modelFailed", "Не вдалося завантажити модель: {0}", "Could not download the model: {0}", "Не удалось загрузить модель: {0}");

        A("svc.exeMissing", "Не знайдено AsmodayCat.Service.exe поруч із програмою.",
                            "AsmodayCat.Service.exe was not found next to the app.",
                            "Не найден AsmodayCat.Service.exe рядом с программой.");
        A("svc.starting", "Запуск служби AsmodayCat…", "Starting the AsmodayCat service…", "Запуск службы AsmodayCat…");
        A("svc.startFailed", "Не вдалося запустити службу: {0}", "Could not start the service: {0}", "Не удалось запустить службу: {0}");
        A("svc.timeout", "Служба не відповіла за 30 с.", "The service did not respond within 30 s.", "Служба не ответила за 30 с.");
        A("svc.restarting", "Перезапуск служби…", "Restarting the service…", "Перезапуск службы…");

        A("app.crash", "Сталася помилка: {0}\n\nДеталі записано в журнал:\n{1}",
                       "An error occurred: {0}\n\nDetails were written to the log:\n{1}",
                       "Произошла ошибка: {0}\n\nПодробности записаны в журнал:\n{1}");
    }
}
