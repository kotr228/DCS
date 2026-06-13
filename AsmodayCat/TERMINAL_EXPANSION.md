# ASMODAYCAT - TERMINAL & CLI EXPANSION (PHASE 11)

## 1. Огляд завдання
Поточний `TerminalView` відображає лише базовий потік тексту. Необхідно перетворити його на повноцінну інтерактивну консоль адміністратора з підтримкою кольорового форматування (залежно від рівня логування), фільтрацією подій, автоскролом та полем для вводу прямих сервісних команд (CLI).

## 2. Функціональні вимоги (FR-T)
* **FR-T1 (Structured Logging):** Логи повинні мати рівні (Info, Warning, Error, AgentAction, Network). Кожен рівень має свій колір у терміналі (наприклад, Error — червоний, AgentAction — ціан).
* **FR-T2 (Filtering & Controls):** Над або під терміналом повинні бути Toggle-кнопки для увімкнення/вимкнення відображення певних рівнів логів, кнопка "Clear" та перемикач "Auto-Scroll" (щоб зупинити прокрутку, коли треба прочитати конкретний рядок).
* **FR-T3 (Interactive CLI):** Внизу терміналу має бути поле вводу (Command Line) для відправки прямих команд у фоновий сервіс (наприклад: `/restart`, `/kill_all`, `/ping_coffeecat`, `/status`).
* **FR-T4 (Export):** Кнопка експорту поточного буфера логів у файл `.log` або `.txt`.

---

## 3. ДЕКОМПОЗИЦІЯ ТА ПОКРОКОВІ ТАСКИ (ІНСТРУКЦІЯ ДЛЯ CLAUDE)

**@Claude:** Це інструкція для перетворення простого логера на інтерактивний термінал. Виконуй кроки послідовно.

### Step 1: DTO та Рівні логування (AsmodayCat.Shared)
1. Створи `LogEntryDto`:
   * `DateTime Timestamp`
   * `LogLevel Level` (Enum: Info, Warning, Error, Agent, System, Network)
   * `string Message`
   * `string Source` (Модуль, який згенерував лог)

### Step 2: Маршрутизація логів та CLI-команд (AsmodayCat.Service)
1. Налаштуй перехоплення логів із `ILogger` або створи кастомний `EventBus` у сервісі, який збирає всі логи та відправляє їх через IPC до UI у форматі `LogEntryDto`.
2. Реалізуй обробник IPC для прямих CLI команд (`ExecuteCliCommand(string command)`).
   * Додай базовий парсер (якщо команда починається з `/`, розбивати на аргументи).
   * Реалізуй базові команди: `/clear`, `/ping`, `/help`.
   * Команда повинна повертати текстову відповідь, яка одразу друкується в термінал з рівнем `System`.

### Step 3: ViewModel (AsmodayCat.UI)
1. Відкрий `TerminalViewModel`.
2. Додай `ObservableCollection<LogEntryDto> Logs`. Застосуй `CollectionViewSource` для реалізації фільтрації (щоб UI не фрізив при великій кількості записів).
3. Додай `ObservableProperty` для фільтрів: `ShowInfo`, `ShowErrors`, `ShowAgentTasks` тощо.
4. Додай `CurrentCommand` (зв'язано з полем вводу) та команду `SendCommand`, яка відправляє текст у сервіс і очищає поле.
5. Реалізуй `ExportLogsCommand` (збереження поточного відфільтрованого списку у файл).

### Step 4: Верстка XAML (AsmodayCat.UI)
1. У `TerminalView.xaml` використовуй `ItemsControl` (всередині `ScrollViewer`) або `ListBox` із налаштованим `DataTemplate` замість простого `TextBox`.
2. Налаштуй тригери (DataTriggers) у `DataTemplate`, щоб змінювати колір тексту (Foreground) залежно від `LogLevel` (наприклад, червоний для `Error`, сірий для `Info`, жовтий для `Warning`).
3. Додай панель інструментів (ToolBar) з чекбоксами для фільтрів, кнопкою "Auto-Scroll" та "Export".
4. Внизу додай поле вводу `TextBox` для CLI команд (шрифт Consolas / Monospace) із кнопкою "Execute".