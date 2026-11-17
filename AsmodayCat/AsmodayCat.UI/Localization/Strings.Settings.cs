namespace AsmodayCat.UI.Localization;

public static partial class Strings
{
    private static void Settings()
    {
        A("settings.title", "Налаштування",
            "Settings",
            "Настройки");
        A("settings.subtitle", "Відеокарта, процесор, підключення та запуск разом з Windows",
            "Graphics card, processor, connections and starting with Windows",
            "Видеокарта, процессор, подключения и запуск вместе с Windows");
        A("settings.gpuCard", "Відеокарта й моделі",
            "Graphics card & models",
            "Видеокарта и модели");
        A("settings.afterSave", "Діє після «Зберегти»",
            "Applies after “Save”",
            "Действует после «Сохранить»");
        A("settings.device", "Де запускати моделі",
            "Where to run models",
            "Где запускать модели");
        A("settings.context", "Мінімальний розмір контексту",
            "Minimum context size",
            "Минимальный размер контекста");
        A("settings.contextHelp", "Більше — довша пам’ять розмови, але більше відеопам’яті. Під час розмови росте сам.",
            "Larger means longer conversation memory but more video memory. It grows by itself during a conversation.",
            "Больше — длиннее память разговора, но больше видеопамяти. Во время разговора растёт сам.");
        A("settings.vramLimit", "Ліміт відеопам’яті для моделі",
            "Video memory limit for a model",
            "Лимит видеопамяти для модели");
        A("settings.noLimit", "Без ліміту",
            "No limit",
            "Без лимита");
        A("settings.vramHelp", "0 — без ліміту; інакше частина моделі працюватиме на процесорі",
            "0 means no limit; otherwise part of the model runs on the processor",
            "0 — без лимита; иначе часть модели будет работать на процессоре");
        A("settings.cpuCard", "Процесор",
            "Processor",
            "Процессор");
        A("settings.threads", "Потоки процесора",
            "Processor threads",
            "Потоки процессора");
        A("settings.fallbackTip", "Вимкнено: якщо модель не влазить у вільну VRAM, буде помилка замість повільної роботи на процесорі",
            "Off: if a model doesn't fit into free VRAM, you get an error instead of slow work on the processor",
            "Выключено: если модель не помещается в свободную VRAM, будет ошибка вместо медленной работы на процессоре");
        A("settings.fallback", "Дозволити процесор, якщо не вистачає VRAM",
            "Allow the processor when VRAM is short",
            "Разрешить процессор, если не хватает VRAM");
        A("settings.fallbackHelp", "Повільніше, зате модель запуститься завжди",
            "Slower, but the model always starts",
            "Медленнее, зато модель запустится всегда");
        A("settings.connCard", "Підключення",
            "Connections",
            "Подключения");
        A("settings.connHelp", "Застосовуються після перезапуску фонової служби",
            "Applied after the background service restarts",
            "Применяются после перезапуска фоновой службы");
        A("settings.ollamaHint", "Адреса Ollama (типово http://localhost:11434)",
            "Ollama address (default http://localhost:11434)",
            "Адрес Ollama (по умолчанию http://localhost:11434)");
        A("settings.sdHint", "Адреса Stable Diffusion WebUI (типово http://127.0.0.1:7860)",
            "Stable Diffusion WebUI address (default http://127.0.0.1:7860)",
            "Адрес Stable Diffusion WebUI (по умолчанию http://127.0.0.1:7860)");
        A("settings.workspaceHint", "Тека помічника (файли, які він створює без запиту)",
            "Assistant folder (files it creates without asking)",
            "Папка помощника (файлы, которые он создаёт без запроса)");
        A("settings.saveRestart", "Зберегти й перезапустити службу",
            "Save and restart the service",
            "Сохранить и перезапустить службу");
        A("settings.systemCard", "Система",
            "System",
            "Система");
        A("settings.autostart", "Запускати разом з Windows",
            "Start with Windows",
            "Запускать вместе с Windows");
        A("settings.autostartHelp", "Фонова служба стартує при вході в систему: агенти в папках працюють навіть із закритим вікном. Діє одразу.",
            "The background service starts when you sign in: folder agents work even with the window closed. Applies immediately.",
            "Фоновая служба стартует при входе в систему: агенты в папках работают даже с закрытым окном. Действует сразу.");
        A("settings.languageCard", "Мова",
            "Language",
            "Язык");
        A("settings.languageHelp", "Мова інтерфейсу, повідомлень служби та голосу. Помічник відповідає мовою вашого повідомлення.",
            "Language of the interface, service messages and voice. The assistant answers in the language you write in.",
            "Язык интерфейса, сообщений службы и голоса. Помощник отвечает на языке вашего сообщения.");
        A("fmt.tokens", "{0:N0} токенів",
            "{0:N0} tokens",
            "{0:N0} токенов");
        A("fmt.mbPlain", "{0} МБ",
            "{0} MB",
            "{0} МБ");
        A("fmt.threads", "{0} потоків",
            "{0} threads",
            "{0} потоков");
        A("fmt.maxThreads", "Максимум: {0} логічних потоків",
            "Maximum: {0} logical threads",
            "Максимум: {0} логических потоков");
        A("settings.version", "AsmodayCat {0} · журнали: {1}",
            "AsmodayCat {0} · logs: {1}",
            "AsmodayCat {0} · журналы: {1}");
        A("settings.saved", "Збережено",
            "Saved",
            "Сохранено");
        A("settings.savedRestarted", "Збережено, службу перезапущено.",
            "Saved; the service was restarted.",
            "Сохранено, служба перезапущена.");
        A("settings.savedNotStarted", "Збережено, але служба не запустилася — див. журнал.",
            "Saved, but the service did not start — see the log.",
            "Сохранено, но служба не запустилась — см. журнал.");
        A("settings.autostartOn", "Служба запускатиметься разом з Windows.",
            "The service will start with Windows.",
            "Служба будет запускаться вместе с Windows.");
        A("settings.autostartOff", "Автозапуск вимкнено.",
            "Autostart is off.",
            "Автозапуск выключен.");
        A("settings.autostartNoExe", "Не вдалося знайти AsmodayCat.Service.exe для автозапуску.",
            "Could not find AsmodayCat.Service.exe for autostart.",
            "Не удалось найти AsmodayCat.Service.exe для автозапуска.");
        A("settings.threadsRange", "Потоків процесора: від 1 до {0}",
            "Processor threads: from 1 to {0}",
            "Потоков процессора: от 1 до {0}");
        A("settings.contextRange", "Розмір контексту: від 2048 до 32768",
            "Context size: from 2048 to 32768",
            "Размер контекста: от 2048 до 32768");
    }
}
