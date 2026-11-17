namespace GrayCat.UI.Localization;

/// <summary>
/// Interface texts: English text (the key, used as is for English) → Ukrainian.
/// Split by area into partial files; a missing entry simply shows the English text.
/// </summary>
public static partial class Strings
{
    public static readonly Dictionary<string, string> Uk = new(StringComparer.Ordinal);

    static Strings()
    {
        Common();
        MainWindow();
        Messages();
        Status();
        Panel();
        Dialogs();
        BlocksAndValues();
    }

    /// <summary>Block type names (Loc.Block) and option values shown in combo boxes (Loc.Value).</summary>
    private static void BlocksAndValues()
    {
        void Block(string type, string uk) => Uk["block:" + type] = uk;
        void Value(string value, string uk) => Uk["value:" + value] = uk;

        A("{0} block", "Блок «{0}»");

        Block("Text", "Текст");
        Block("Header", "Заголовок");
        Block("Navbar", "Меню навігації");
        Block("Hero", "Банер");
        Block("Button", "Кнопка");
        Block("Card", "Картка");
        Block("Image", "Зображення");
        Block("Gallery", "Галерея");
        Block("ProductGrid", "Товари");
        Block("Form", "Форма");
        Block("ContactForm", "Форма зв'язку");
        Block("Video", "Відео");
        Block("Testimonial", "Відгук");
        Block("Pricing", "Тариф");
        Block("Accordion", "Питання й відповіді");
        Block("Stats", "Статистика");
        Block("SocialLinks", "Соцмережі");
        Block("Divider", "Розділювач");
        Block("Footer", "Підвал");

        Value("Left", "Ліворуч");
        Value("Center", "По центру");
        Value("Right", "Праворуч");
        Value("Justify", "За шириною");
        Value("left", "ліворуч");
        Value("center", "по центру");
        Value("right", "праворуч");
        Value("top", "зверху");
        Value("bottom", "знизу");
        Value("small", "малий");
        Value("medium", "середній");
        Value("large", "великий");
        Value("full", "на весь екран");
        Value("primary", "основна");
        Value("secondary", "другорядна");
        Value("success", "успіх (зелена)");
        Value("danger", "небезпека (червона)");
        Value("warning", "попередження (жовта)");
        Value("info", "інформація (блакитна)");
        Value("_self", "цій самій вкладці");
        Value("_blank", "новій вкладці");
        Value("static", "звичайне");
        Value("fixed", "закріплене вгорі");
        Value("sticky", "прилипає під час прокрутки");
        Value("default", "звичайний");
        Value("card", "картка");
        Value("minimal", "мінімальний");
        Value("horizontal", "в рядок");
        Value("vertical", "стовпчиком");
        Value("grid", "сіткою");
        Value("icons", "значки");
        Value("buttons", "кнопки");
        Value("text", "текст");
        Value("solid", "суцільна");
        Value("dashed", "штрихова");
        Value("dotted", "крапкова");
        Value("double", "подвійна");
    }

    private static void A(string en, string uk) => Uk[en] = uk;

    private static void Common()
    {
        A("OK", "OK");
        A("Yes", "Так");
        A("No", "Ні");
        A("Cancel", "Скасувати");
        A("Save", "Зберегти");
        A("Error", "Помилка");
        A("Apply", "Застосувати");
        A("Delete", "Видалити");
        A("Duplicate", "Дублювати");
        A("Confirm", "Підтвердження");
        A("Browse…", "Огляд…");
        A("Browse...", "Огляд...");
        A("Validation", "Перевірка");
        A("Language", "Мова");
        A("Interface language", "Мова інтерфейсу");
    }

    private static void MainWindow()
    {
        // Menu
        A("_File", "_Файл");
        A("_New Project...", "_Новий проєкт...");
        A("_Open Project...", "_Відкрити проєкт...");
        A("_Save Project", "_Зберегти проєкт");
        A("Open _Code Folder", "Відкрити папку з _кодом");
        A("Export Generated Code as _ZIP...", "Експортувати згенерований код у _ZIP...");
        A("Open _Recent", "_Останні проєкти");
        A("E_xit", "В_ихід");
        A("_Edit", "_Правка");
        A("_Undo", "_Скасувати");
        A("_Redo", "_Повторити");
        A("Cu_t", "Ви_різати");
        A("_Copy", "_Копіювати");
        A("_Paste", "Вст_авити");
        A("_Duplicate", "_Дублювати");
        A("_Delete", "Ви_далити");
        A("_Project", "Про_єкт");
        A("_Run App", "_Запустити застосунок");
        A("_Stop App", "З_упинити застосунок");
        A("Show Run _Log", "_Журнал запуску");
        A("_Generate Code", "_Згенерувати код");
        A("_Build for Production", "_Зібрати для публікації");
        A("Static _HTML Preview", "Статичний _HTML-перегляд");
        A("_Export Static Site...", "_Експортувати статичний сайт...");
        A("_Arrange Blocks in a Column", "Вишикувати блоки в _колонку");
        A("_Validate All Blocks", "_Перевірити всі блоки");
        A("_Pages...", "_Сторінки...");
        A("Project _Settings and Theme...", "_Налаштування проєкту й тема...");
        A("_Help", "_Довідка");
        A("_Documentation", "_Як користуватися");
        A("Open .graycat Files with GrayCat Studio", "Відкривати файли .graycat у GrayCat Studio");
        A("_About GrayCat", "_Про GrayCat");

        // Toolbar
        A("▶  Run App", "▶  Запустити");
        A("Install packages if needed, start the project and open it in the browser (F5)",
          "Встановити пакети за потреби, запустити проєкт і відкрити його в браузері (F5)");
        A("■  Stop", "■  Зупинити");
        A("Generate Code", "Згенерувати код");
        A("Write the project files into the project folder (Ctrl+G)", "Записати файли проєкту в папку з кодом (Ctrl+G)");
        A("Run Log", "Журнал");
        A("Page:", "Сторінка:");
        A("Page being edited", "Сторінка, яку ви редагуєте");
        A("Pages…", "Сторінки…");
        A("Add, rename, reorder or delete pages", "Додати, перейменувати, впорядкувати або видалити сторінки");
        A("Undo", "Скасувати");
        A("Redo", "Повторити");
        A("Project type: {0}", "Тип проєкту: {0}");

        // Block library
        A("Block Library", "Бібліотека блоків");
        A("Click to add, drag to place", "Клік — додати, перетягування — розмістити");
        A("📐 Layout", "📐 Каркас");
        A("🏠 Hero Section", "🏠 Головний банер");
        A("📄 Header", "📄 Заголовок");
        A("🧭 Navigation Bar", "🧭 Меню навігації");
        A("📝 Text Block", "📝 Текст");
        A("🦶 Footer", "🦶 Підвал");
        A("➖ Divider", "➖ Розділювач");
        A("📝 Content", "📝 Вміст");
        A("🃏 Card", "🃏 Картка");
        A("💬 Testimonial", "💬 Відгук");
        A("❓ Accordion/FAQ", "❓ Питання й відповіді");
        A("📊 Statistics", "📊 Статистика");
        A("💰 Pricing Card", "💰 Тариф");
        A("🎬 Media", "🎬 Медіа");
        A("🖼️ Image", "🖼️ Зображення");
        A("🎥 Video Player", "🎥 Відео");
        A("🎨 Gallery", "🎨 Галерея");
        A("⚡ Interactive", "⚡ Інтерактив");
        A("🔘 Button", "🔘 Кнопка");
        A("📋 Contact Form", "📋 Форма зв'язку");
        A("📝 Form", "📝 Форма");
        A("🛒 Product Grid", "🛒 Товари");
        A("🌐 Social", "🌐 Соцмережі");
        A("📱 Social Links", "📱 Посилання на соцмережі");
        A("💡 Tip", "💡 Порада");
        A("Click a block to add it after the selected one, or drag it onto the Layout canvas. Blocks appear on the page from top to bottom.",
          "Клікніть блок, щоб додати його після виділеного, або перетягніть на полотно «Розміщення». На сторінці блоки йдуть згори донизу.");

        // Welcome screen
        A("Welcome to GrayCat Studio", "Вітаємо в GrayCat Studio");
        A("Build websites and apps from visual blocks", "Створюйте сайти й застосунки з візуальних блоків");
        A("📄 New Project", "📄 Новий проєкт");
        A("📂 Open Project", "📂 Відкрити проєкт");
        A("Recent projects", "Останні проєкти");

        // Editor area
        A("Design (live page)", "Дизайн (жива сторінка)");
        A("Layout (arrange blocks)", "Розміщення блоків");
        A("Device:", "Пристрій:");
        A("Desktop (full width)", "Комп'ютер (на всю ширину)");
        A("Tablet (820 px)", "Планшет (820 px)");
        A("Phone (390 px)", "Телефон (390 px)");
        A("Show grid (snap to 20px)", "Сітка (прилипання до 20 px)");
        A("✏️ Edit Properties", "✏️ Властивості");
        A("✓ Validate", "✓ Перевірити");
        A("📋 Duplicate", "📋 Дублювати");
        A("🗑️ Delete", "🗑️ Видалити");
        A("Blocks: {0}", "Блоків: {0}");
        A("No Project", "Немає проєкту");
        A("Ready", "Готово");
    }

    private static void Messages()
    {
        A("Could not open the project:\n\n{0}", "Не вдалося відкрити проєкт:\n\n{0}");
        A("Could not create the project", "Не вдалося створити проєкт");
        A("Could not create the project:\n\n{0}", "Не вдалося створити проєкт:\n\n{0}");
        A("Open Project", "Відкрити проєкт");
        A("The current project could not be saved, so it was kept open.", "Поточний проєкт не вдалося зберегти, тому він лишився відкритим.");
        A("Double-clicking a .graycat file now opens it in GrayCat Studio.", "Тепер подвійний клік по файлу .graycat відкриває його в GrayCat Studio.");
        A("File association", "Асоціація файлів");
        A("Could not register .graycat files:\n\n{0}", "Не вдалося зареєструвати файли .graycat:\n\n{0}");
        A("The project file no longer exists:\n{0}", "Файлу проєкту більше немає:\n{0}");
        A("Open Recent", "Останні проєкти");
        A("{0}\n\nClose without saving?", "{0}\n\nЗакрити без збереження?");
        A("{0} block(s) have validation problems (see Project → Validate All Blocks).\n\nGenerate anyway?",
          "Блоків із проблемами: {0} (див. Проєкт → Перевірити всі блоки).\n\nВсе одно згенерувати?");
        A("Code generation failed", "Не вдалося згенерувати код");
        A("Code generated in:\n{0}\n\n{1} files written in {2} ms.\n", "Код згенеровано в:\n{0}\n\nЗаписано файлів: {1} за {2} мс.\n");
        A("{0}{1}\n\nOpen the project folder?", "{0}{1}\n\nВідкрити папку з кодом?");
        A("Code generation failed:\n\n{0}", "Не вдалося згенерувати код:\n\n{0}");
        A("Run App", "Запуск застосунку");
        A("Could not run the app:\n\n{0}", "Не вдалося запустити застосунок:\n\n{0}");
        A("Choose an image", "Виберіть зображення");
        A("Choose a video", "Виберіть відео");
        A("Could not copy the image:\n\n{0}", "Не вдалося скопіювати зображення:\n\n{0}");
        A("Build for Production", "Збирання для публікації");
        A("\n\nOpen the folder?", "\n\nВідкрити папку?");
        A("The build failed:\n\n{0}", "Збирання не вдалося:\n\n{0}");
        A("Save the project as a ZIP file", "Зберегти код як ZIP-архів");
        A("Saved {0}\n\nInstalled packages (node_modules) and build output are not included; run npm install after unpacking.",
          "Збережено {0}\n\nВстановлені пакети (node_modules) і результати збирання не включено; після розпакування виконайте npm install.");
        A("Export ZIP", "Експорт у ZIP");
        A("Could not create the ZIP file:\n\n{0}", "Не вдалося створити ZIP-файл:\n\n{0}");
        A("Could not open the preview:\n\n{0}", "Не вдалося відкрити перегляд:\n\n{0}");
        A("Preview", "Перегляд");
        A("Choose a folder for the static site (index.html, styles.css, script.js)", "Виберіть папку для статичного сайту (index.html, styles.css, script.js)");
        A("This folder already has an index.html. Replace it?", "У цій папці вже є index.html. Замінити?");
        A("Export", "Експорт");
        A("Static site exported to:\n{0}\n\nOpen it in the browser?", "Статичний сайт експортовано в:\n{0}\n\nВідкрити в браузері?");
        A("Export failed:\n\n{0}", "Експорт не вдався:\n\n{0}");
        A("Project type changed to {0}. Use Generate Code to write the new project.", "Тип проєкту змінено на {0}. Натисніть «Згенерувати код», щоб записати новий проєкт.");
        A("No blocks to validate.", "Немає блоків для перевірки.");
        A("✓ Valid blocks: {0}\n✗ Blocks with problems: {1}", "✓ Правильних блоків: {0}\n✗ Блоків із проблемами: {1}");
        A("Validation Results", "Результати перевірки");
        A("\n\nAll blocks are valid!", "\n\nУсі блоки правильні!");
        A("Create or open a project first.", "Спершу створіть або відкрийте проєкт.");
        A("✓ {0} block is valid!", "✓ Блок {0} правильний!");
        A("Problems in the {0} block:\n\n• {1}", "Проблеми в блоці {0}:\n\n• {1}");
        A("Delete the {0} block?", "Видалити блок {0}?");
        A("Confirm Delete", "Підтвердження видалення");
        A("Open index.html in a browser.", "Відкрийте index.html у браузері.");
        A("Run: npm install, then npm run dev", "Запуск: npm install, потім npm run dev");
        A("Run: npm install, then npm start (http://localhost:3000)", "Запуск: npm install, потім npm start (http://localhost:3000)");
        A("Run: npm install, then npx expo start", "Запуск: npm install, потім npx expo start");
        A("Could not open {0}:\n{1}", "Не вдалося відкрити {0}:\n{1}");
        A("Something went wrong:\n\n{0}\n\nYour project is saved automatically.", "Щось пішло не так:\n\n{0}\n\nВаш проєкт зберігається автоматично.");

        // Help and About
        A("How to use GrayCat Studio", "Як користуватися GrayCat Studio");
        A("1. File → New Project: pick a name, folder, project type and template.\n", "1. Файл → Новий проєкт: виберіть назву, папку, тип проєкту й шаблон.\n");
        A("   The project is saved as one .graycat file (pages, blocks, theme and your images).\n", "   Проєкт зберігається одним файлом .graycat (сторінки, блоки, тема й ваші зображення).\n");
        A("   Code is written to the folder with the same name next to it, only when you generate, run or build.\n",
          "   Код записується в однойменну папку поруч — лише коли ви генеруєте, запускаєте чи збираєте.\n");
        A("2. Drag blocks from the library onto the canvas (or click them).\n", "2. Перетягніть блоки з бібліотеки на полотно (або клікніть по них).\n");
        A("   Blocks appear on the page top-to-bottom, in canvas order.\n", "   На сторінці блоки йдуть згори донизу, у порядку на полотні.\n");
        A("3. Select a block to edit it in the right-hand panel. Changes save automatically.\n", "3. Виділіть блок, щоб редагувати його на панелі праворуч. Зміни зберігаються автоматично.\n");
        A("   Use Browse… to add your own images: they are copied into the project's assets folder.\n", "   Кнопка «Огляд…» додає ваші зображення: вони копіюються в проєкт.\n");
        A("   Drag the corner handle of a selected block to resize it.\n", "   Щоб змінити розмір, тягніть маркер у куті виділеного блоку.\n");
        A("4. The Design tab shows the real page; click a block there to select it.\n", "4. Вкладка «Дизайн» показує справжню сторінку; клік по блоку виділяє його.\n");
        A("   Layout lets you arrange blocks freely (top-to-bottom order = page order).\n", "   «Розміщення» дає вільно розставити блоки (порядок згори донизу = порядок на сторінці).\n");
        A("5. Run App (F5) generates the project, installs packages and starts it in your browser.\n", "5. «Запустити» (F5) генерує проєкт, встановлює пакети й відкриває його в браузері.\n");
        A("   While it runs, every change you make updates the running app. Stop with Shift+F5.\n", "   Поки він працює, кожна зміна одразу потрапляє в застосунок. Зупинка — Shift+F5.\n");
        A("   Generate Code (Ctrl+G) only writes the files.\n", "   «Згенерувати код» (Ctrl+G) лише записує файли.\n");
        A("6. Pages: pick the page to edit in the toolbar; Pages… adds, renames and deletes pages.\n", "6. Сторінки: виберіть сторінку на панелі інструментів; «Сторінки…» додає, перейменовує й видаляє їх.\n");
        A("   Project → Settings and Theme sets colours and the font for the whole site.\n\n", "   Проєкт → Налаштування й тема задає кольори та шрифт для всього сайту.\n\n");
        A("Project types:\n", "Типи проєктів:\n");
        A("• Static website (HTML) - open index.html, no build needed\n", "• Статичний сайт (HTML) — відкрийте index.html, збирання не потрібне\n");
        A("• React website (Vite) - npm install, npm run dev\n", "• Сайт на React (Vite) — npm install, npm run dev\n");
        A("• Next.js website - with a form API (npm install, npm run dev)\n", "• Сайт на Next.js — з API для форм (npm install, npm run dev)\n");
        A("• Website with a Node.js server - Express serves the site and stores form submissions\n", "• Сайт із сервером Node.js — Express віддає сайт і зберігає заявки з форм\n");
        A("• Mobile app (React Native, Expo) - npm install, npx expo start\n\n", "• Мобільний застосунок (React Native, Expo) — npm install, npx expo start\n\n");
        A("Shortcuts: Ctrl+N/O/S, Ctrl+Shift+S (save as), Ctrl+Z/Y (undo/redo), Ctrl+G, F5, Ctrl+C/X/V/D, Del, arrows (Shift = 10px).",
          "Клавіші: Ctrl+N/O/S, Ctrl+Shift+S (зберегти як), Ctrl+Z/Y (скасувати/повторити), Ctrl+G, F5, Ctrl+C/X/V/D, Del, стрілки (Shift = 10 px).");
        A("Save Project _As...", "Зберегти проєкт _як...");
        A("Save Project As", "Зберегти проєкт як");
        A("Saved as {0}", "Збережено як {0}");
        A("GrayCat project", "Проєкт GrayCat");
        A("GrayCat 1.0 project", "Проєкт GrayCat 1.0");
        A("ZIP archive", "Архів ZIP");
        A("Videos", "Відео");
        A("Images", "Зображення");
        A("All files", "Усі файли");
        A("About GrayCat Studio", "Про GrayCat Studio");
        A("GrayCat Studio {0}\n\n", "GrayCat Studio {0}\n\n");
        A("Visual website and app builder with block-based design.\n", "Візуальний конструктор сайтів і застосунків з блоків.\n");
        A("Generates static HTML, React, Next.js, Node.js and React Native projects.\n\n", "Генерує проєкти: статичний HTML, React, Next.js, Node.js і React Native.\n\n");
        A("Built with WPF and .NET 8.", "Створено на WPF і .NET 8.");
    }

    private static void Status()
    {
        A("The app stopped (exit code {0}). See Project → Run Log.", "Застосунок зупинився (код {0}). Див. Проєкт → Журнал запуску.");
        A("Editing page: {0}", "Редагується сторінка: {0}");
        A("The current project could not be saved.", "Поточний проєкт не вдалося зберегти.");
        A("{0} already exists.", "{0} уже існує.");
        A("Created {0}", "Створено {0}");
        A("Project created", "Проєкт створено");
        A("Converted to the new format: {0}", "Перетворено в новий формат: {0}");
        A("Opened {0}", "Відкрито {0}");
        A("Saved at {0:HH:mm:ss}", "Збережено о {0:HH:mm:ss}");
        A("Save failed: {0}", "Не вдалося зберегти: {0}");
        A("Open or create a project first.", "Спершу відкрийте або створіть проєкт.");
        A("Generating code...", "Генерується код...");
        A("Code generated: {0} files ({1}, {2} ms)", "Код згенеровано: файлів {0} ({1}, {2} мс)");
        A("Open a project first.", "Спершу відкрийте проєкт.");
        A("Node.js is needed to run this project but was not found.\n\n", "Для запуску цього проєкту потрібен Node.js, але його не знайдено.\n\n");
        A("Install the LTS version from https://nodejs.org, restart GrayCat Studio and try again.\n", "Встановіть LTS-версію з https://nodejs.org, перезапустіть GrayCat Studio і спробуйте ще раз.\n");
        A("Meanwhile, Project → Static HTML Preview shows the page without Node.js.", "А поки що Проєкт → Статичний HTML-перегляд показує сторінку без Node.js.");
        A("Already running", "Уже запущено");
        A("Opened index.html", "Відкрито index.html");
        A("Installing packages with npm (the first run can take a few minutes)...", "Встановлюються пакети npm (перший запуск може тривати кілька хвилин)...");
        A("npm install failed", "npm install не вдався");
        A("npm install failed. Open Project → Run Log for details.", "npm install не вдався. Подробиці: Проєкт → Журнал запуску.");
        A("Starting the app...", "Застосунок запускається...");
        A("The app did not start", "Застосунок не запустився");
        A("The app did not start. Open Project → Run Log for details.", "Застосунок не запустився. Подробиці: Проєкт → Журнал запуску.");
        A("App running at {0}. Your edits update it automatically.", "Застосунок працює на {0}. Ваші зміни потрапляють у нього автоматично.");
        A("Running", "Працює");
        A("Node.js is needed to build this project. Install the LTS version from https://nodejs.org and restart GrayCat Studio.",
          "Для збирання цього проєкту потрібен Node.js. Встановіть LTS-версію з https://nodejs.org і перезапустіть GrayCat Studio.");
        A("Static sites need no build. Upload the .html files, styles.css, script.js and the assets folder to any web host.",
          "Статичний сайт не потребує збирання. Завантажте файли .html, styles.css, script.js і папку assets на будь-який хостинг.");
        A("Node.js projects run as they are: copy the folder to the server, run npm install --omit=dev and npm start.",
          "Проєкт Node.js запускається як є: скопіюйте папку на сервер, виконайте npm install --omit=dev і npm start.");
        A("Installing packages with npm...", "Встановлюються пакети npm...");
        A("Building for production...", "Збирання для публікації...");
        A("Build failed", "Збирання не вдалося");
        A("The build failed. Open Project → Run Log for details.", "Збирання не вдалося. Подробиці: Проєкт → Журнал запуску.");
        A("Production build ready in {0}", "Версія для публікації готова в {0}");
        A("Build ready in {0}.\nRun it on a server with: npm start", "Готово в {0}.\nНа сервері запускайте командою: npm start");
        A("Build ready in {0}.\nUpload the contents of this folder to any static web host ", "Готово в {0}.\nЗавантажте вміст цієї папки на будь-який статичний хостинг ");
        A("(set it to serve index.html for unknown paths so page links like /about keep working).",
          "(налаштуйте, щоб на невідомі адреси віддавався index.html — тоді посилання на кшталт /about працюватимуть).");
        A("App stopped", "Застосунок зупинено");
        A("Saved. Running app updated at {0:HH:mm:ss}", "Збережено. Застосунок оновлено о {0:HH:mm:ss}");
        A("Saved, but the running app could not be updated: {0}", "Збережено, але застосунок не вдалося оновити: {0}");
        A("Preview opened in your browser", "Перегляд відкрито в браузері");
        A("Exported to {0}", "Експортовано в {0}");
        A("Added {0} to the project", "Додано {0} до проєкту");
        A("Added {0} block", "Додано блок {0}");
        A("Deleted {0} block", "Видалено блок {0}");
        A("{0} block copied", "Блок {0} скопійовано");
        A("Blocks arranged", "Блоки вишикувано");
        A("Moved block up", "Блок переміщено вище");
        A("Moved block down", "Блок переміщено нижче");
        A("Added copy of {0} block", "Додано копію блоку {0}");
        A("Unsaved changes", "Є незбережені зміни");
        A("Undone", "Скасовано");
        A("Redone", "Повторено");
        A("Added page {0}", "Додано сторінку {0}");
        A("Updated page {0}", "Оновлено сторінку {0}");
        A("Deleted page {0}", "Видалено сторінку {0}");
        A("Reordered pages", "Порядок сторінок змінено");
        A("Project saved as {0}", "Збережено як {0}");
        A("Snapping blocks to a 20px grid", "Блоки прилипають до сітки 20 px");
        A("Grid snapping off", "Прилипання до сітки вимкнено");
    }

    private static void Panel()
    {
        A("Select a block on the canvas to edit it,\nor drag a new one from the library.", "Виділіть блок, щоб його редагувати,\nабо перетягніть новий із бібліотеки.");
        A("Content", "Вміст");
        A("On the page", "На сторінці");
        A("Section id (menu links like #contact jump here)", "Ідентифікатор секції (посилання на кшталт #contact ведуть сюди)");
        A("Hide on the published site", "Сховати на опублікованому сайті");
        A("Appearance", "Вигляд");
        A("Text color", "Колір тексту");
        A("Background", "Тло");
        A("Font size", "Розмір шрифту");
        A("Text alignment", "Вирівнювання тексту");
        A("Font", "Шрифт");
        A("Corner radius", "Заокруглення кутів");
        A("Canvas position (sets the order on the page)", "Положення на полотні (задає порядок на сторінці)");
        A("Width", "Ширина");
        A("Height", "Висота");
        A("Text", "Текст");
        A("Title", "Заголовок");
        A("Subtitle", "Підзаголовок");
        A("Show navigation links", "Показувати посилання навігації");
        A("List the site's pages in the menu", "Показувати сторінки сайту в меню");
        A("Edit navigation links", "Редагувати посилання навігації");
        A("Brand name", "Назва бренду");
        A("Logo", "Логотип");
        A("Position", "Розташування");
        A("Transparent", "Прозоре");
        A("Show search", "Показувати пошук");
        A("Button text (optional)", "Текст кнопки (необов'язково)");
        A("Button URL", "Посилання кнопки");
        A("Edit menu items", "Редагувати пункти меню");
        A("Headline", "Заголовок");
        A("Subheadline", "Підзаголовок");
        A("Button text", "Текст кнопки");
        A("Background image", "Фонове зображення");
        A("Dark overlay on image", "Затемнення поверх зображення");
        A("Overlay opacity", "Сила затемнення");
        A("Alignment", "Вирівнювання");
        A("Variant", "Варіант");
        A("Size", "Розмір");
        A("Open in", "Відкривати в");
        A("Full width", "На всю ширину");
        A("Disabled", "Неактивна");
        A("Description", "Опис");
        A("Image", "Зображення");
        A("Image position", "Розташування зображення");
        A("Link text", "Текст посилання");
        A("Link URL", "Адреса посилання");
        A("Shadow", "Тінь");
        A("Border", "Рамка");
        A("Lift on hover", "Підйом під курсором");
        A("Alt text", "Опис зображення (alt)");
        A("Caption", "Підпис");
        A("Columns", "Стовпців");
        A("Edit images", "Редагувати зображення");
        A("Currency", "Валюта");
        A("Edit products", "Редагувати товари");
        A("Submit button text", "Текст кнопки надсилання");
        A("Submit URL", "Адреса надсилання");
        A("Success message", "Повідомлення про успіх");
        A("Error message", "Повідомлення про помилку");
        A("Add phone field", "Додати поле телефону");
        A("Add subject field", "Додати поле теми");
        A("Edit form fields", "Редагувати поля форми");
        A("Video (YouTube, Vimeo link or a video file)", "Відео (посилання YouTube, Vimeo або відеофайл)");
        A("Aspect ratio", "Пропорції");
        A("Autoplay (muted)", "Автовідтворення (без звуку)");
        A("Show controls", "Показувати керування");
        A("Loop", "Повтор");
        A("Muted", "Без звуку");
        A("Quote", "Цитата");
        A("Author", "Автор");
        A("Author role", "Посада автора");
        A("Avatar", "Аватар");
        A("Rating", "Оцінка");
        A("Show rating", "Показувати оцінку");
        A("Layout", "Компонування");
        A("Plan name", "Назва тарифу");
        A("Price", "Ціна");
        A("Period", "Період");
        A("Badge (optional)", "Позначка (необов'язково)");
        A("Highlighted", "Виділений");
        A("Edit features", "Редагувати переваги");
        A("Allow several open at once", "Дозволити кілька відкритих одночасно");
        A("Open by default (item #, 0 = first)", "Відкритий за замовчуванням (№, 0 = перший)");
        A("Edit questions", "Редагувати питання");
        A("Edit statistics", "Редагувати статистику");
        A("Style", "Стиль");
        A("Edit links", "Редагувати посилання");
        A("Line style", "Стиль лінії");
        A("Thickness", "Товщина");
        A("Line color (#RRGGBB)", "Колір лінії (#RRGGBB)");
        A("Spacing", "Відступи");
        A("Show icon in the middle", "Значок посередині");
        A("Icon", "Значок");
        A("Copyright text (empty = automatic)", "Текст авторських прав (порожньо = автоматично)");
        A("Edit footer links", "Редагувати посилання підвалу");
        A("Web address (https://...) or a file from the project's assets folder", "Адреса в інтернеті (https://...) або файл із проєкту");
        A("Could not copy the file into the project:\n\n{0}", "Не вдалося скопіювати файл у проєкт:\n\n{0}");
        A("Use #RRGGBB, for example #1E293B", "Формат #RRGGBB, наприклад #1E293B");
        A("Validate block", "Перевірити блок");
        A("✓ Block is valid!", "✓ Блок правильний!");
        A("Problems found:\n\n• {0}", "Знайдено проблеми:\n\n• {0}");
        A("Reset style", "Скинути стиль");
        A("Delete {0} block?", "Видалити блок {0}?");
        A("▲ Move up", "▲ Вище");
        A("Earlier on the page", "Раніше на сторінці");
        A("▼ Move down", "▼ Нижче");
        A("Later on the page", "Пізніше на сторінці");
    }

    private static void Dialogs()
    {
        // Pages
        A("Pages", "Сторінки");
        A("+ New page", "+ Нова сторінка");
        A("Page title (shown in the menu)", "Назва сторінки (показується в меню)");
        A("Address", "Адреса");
        A("Lowercase letters, digits and dashes. The page is available at /address (about.html in static sites).",
          "Малі латинські літери, цифри й дефіси. Сторінка буде за адресою /адреса (about.html у статичних сайтах).");
        A("Show in navigation menus", "Показувати в меню навігації");
        A("Edit this page", "Редагувати цю сторінку");
        A("Delete page", "Видалити сторінку");
        A("(home page)", "(головна сторінка)");
        A("Page {0}", "Сторінка {0}");
        A("Delete the page \"{0}\" and all its blocks?\n\nYou can undo this with Ctrl+Z.", "Видалити сторінку «{0}» і всі її блоки?\n\nЦе можна скасувати через Ctrl+Z.");
        A("The page address cannot be empty (only the home page has no address).", "Адреса сторінки не може бути порожньою (без адреси лише головна).");
        A("Use lowercase letters, digits and dashes, e.g. about-us.", "Використовуйте малі латинські літери, цифри й дефіси, наприклад about-us.");

        // Settings
        A("Project Settings", "Налаштування проєкту");
        A("Site name (page title and brand)", "Назва сайту (заголовок сторінки й бренд)");
        A("Description (shown to search engines)", "Опис (для пошукових систем)");
        A("Project type", "Тип проєкту");
        A("Theme", "Тема");
        A("Main colour (buttons, links, hero)", "Основний колір (кнопки, посилання, банер)");
        A("Accent colour (second colour of the hero gradient)", "Акцентний колір (другий колір градієнта банера)");
        A("Fonts other than System and Georgia load from Google Fonts when the site is opened.", "Шрифти, крім System і Georgia, завантажуються з Google Fonts під час відкриття сайту.");
        A("Site", "Сайт");
        A("Language of the content (helps search engines and screen readers)", "Мова вмісту (допомагає пошуковим системам і програмам читання з екрана)");
        A("Browser tab icon (favicon)", "Значок вкладки браузера (favicon)");
        A("The site name cannot be empty.", "Назва сайту не може бути порожньою.");
        A("Colours must look like #4F46E5.", "Колір має бути у форматі #4F46E5.");
        A("Changing the type makes Generate Code produce a different kind of project in the same folder.",
          "Зміна типу змусить «Згенерувати код» створити інший вид проєкту в тій самій папці.");
        A("Next time you generate code, a {0} project is written into the project folder. ", "Під час наступної генерації в папку буде записано проєкт {0}. ");
        A("Files from the previous {0} project are left in place; delete them if you no longer need them.", "Файли попереднього проєкту {0} лишаться на місці; видаліть їх, якщо вони вже не потрібні.");

        // New project
        A("Create New Project", "Новий проєкт");
        A("Project name", "Назва проєкту");
        A("Location", "Розташування");
        A("Start from", "Почати з");
        A("Store form submissions in SQLite (instead of a JSON file)", "Зберігати заявки з форм у SQLite (замість файлу JSON)");
        A("Create", "Створити");
        A("Choose where to create the project", "Виберіть, де створити проєкт");
        A("Create Project", "Створення проєкту");
        A("Project file: {0}  (code is generated into the folder of the same name)", "Файл проєкту: {0}  (код генерується в однойменну папку)");
        A("Plain HTML/CSS/JS. Open index.html - no tools needed.", "Звичайні HTML/CSS/JS. Відкрийте index.html — інструменти не потрібні.");
        A("React app built with Vite. Needs Node.js: npm install, npm run dev.", "Застосунок React на Vite. Потрібен Node.js: npm install, npm run dev.");
        A("Next.js app with a built-in API for forms. Needs Node.js.", "Застосунок Next.js із вбудованим API для форм. Потрібен Node.js.");
        A("Express server that serves your site and stores form submissions.", "Сервер Express, що віддає ваш сайт і зберігає заявки з форм.");
        A("Mobile app with Expo. Run npx expo start and open it in Expo Go.", "Мобільний застосунок на Expo. Запустіть npx expo start і відкрийте в Expo Go.");
        A("{0} already exists. Open it with File → Open Project, or choose another name.", "{0} уже існує. Відкрийте його через Файл → Відкрити проєкт або виберіть іншу назву.");
        A("The folder {0} (where the code would go) already has other files. Choose another name.", "У папці {0} (куди піде код) уже є інші файли. Виберіть іншу назву.");

        // Templates (names and descriptions come from GrayCat.Core)
        A("Landing page", "Лендинг");
        A("Business site (4 pages)", "Сайт компанії (4 сторінки)");
        A("Portfolio", "Портфоліо");
        A("Online shop", "Інтернет-магазин");
        A("Blank page", "Порожня сторінка");
        A("An empty page to build from scratch.", "Порожня сторінка, щоб почати з нуля.");
        A("One page: navigation, hero, statistics, pricing, contact form and footer.", "Одна сторінка: меню, банер, статистика, тарифи, форма зв'язку й підвал.");
        A("Home, About, Services and Contact pages with a shared menu.", "Сторінки «Головна», «Про нас», «Послуги» й «Контакти» зі спільним меню.");
        A("Hero, gallery, testimonials and a contact form.", "Банер, галерея, відгуки й форма зв'язку.");
        A("Product grid, pricing, FAQ and a newsletter form.", "Товари, тарифи, питання й відповіді та форма розсилки.");

        // Validation messages from GrayCat.Core
        A("Project name cannot be empty.", "Назва проєкту не може бути порожньою.");
        A("Project name must be 100 characters or fewer.", "Назва проєкту має бути не довшою за 100 символів.");
        A("Project name contains characters that are not allowed in a folder name.", "Назва проєкту містить символи, недопустимі в назві папки.");
        A("Project name cannot be '.' or '..'.", "Назва проєкту не може бути «.» або «..».");
        A("Project name cannot start with a space or end with a space or dot.", "Назва проєкту не може починатися з пробілу або закінчуватися пробілом чи крапкою.");
        A("Choose a location for the project.", "Виберіть розташування проєкту.");
        A("Location must be a full path, e.g. C:\\Users\\me\\Documents\\Sites.", "Розташування має бути повним шляхом, наприклад C:\\Users\\me\\Documents\\Sites.");
        A("Location is not a valid path.", "Розташування не є правильним шляхом.");
        A("Projects cannot be created inside system folders.", "Проєкти не можна створювати в системних папках.");

        // List editor, run log, live preview
        A("One entry per item, as a JSON list. Example:\n{0}", "По одному запису на елемент, у вигляді списку JSON. Приклад:\n{0}");
        A("Insert image…", "Вставити зображення…");
        A("The value must be a list: start with [ and end with ].", "Значення має бути списком: починатися з [ і закінчуватися ].");
        A("Invalid JSON (line {0}): {1}.", "Помилка в JSON (рядок {0}): {1}.");
        A("Run Log - GrayCat Studio", "Журнал запуску — GrayCat Studio");
        A("Nothing has run yet. Use Project → Run App (F5).\n", "Ще нічого не запускалося. Скористайтеся Проєкт → Запустити застосунок (F5).\n");
        A("Loading preview…", "Завантаження перегляду…");
        A("The live preview needs the Microsoft Edge WebView2 Runtime.\n", "Для живого перегляду потрібен Microsoft Edge WebView2 Runtime.\n");
        A("Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and restart GrayCat Studio.\n\n", "Встановіть його з https://go.microsoft.com/fwlink/p/?LinkId=2124703 і перезапустіть GrayCat Studio.\n\n");
        A("You can still use the Layout tab and Project → Static HTML Preview.", "Тим часом доступні вкладка «Розміщення» і Проєкт → Статичний HTML-перегляд.");
        A("Preview could not be updated: {0}", "Не вдалося оновити перегляд: {0}");
        A("Static website (HTML)", "Статичний сайт (HTML)");
        A("React website (Vite)", "Сайт на React (Vite)");
        A("Next.js website", "Сайт на Next.js");
        A("Website with a Node.js server", "Сайт із сервером Node.js");
        A("Mobile app (React Native, Expo)", "Мобільний застосунок (React Native, Expo)");
    }
}
