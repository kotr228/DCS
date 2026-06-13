# ASMODAYCAT - AGENT RULES & WORKSPACE MANAGEMENT (PHASE 10)

## 1. Огляд завдання
Вікно `AgentRulesView` наразі містить лише форму додавання одного правила. Необхідно розширити його, щоб система підтримувала **множинні директорії** (агент може одночасно стежити за десятками папок). Крім того, потрібно додати список активних правил для їх редагування/видалення, зняти жорсткі обмеження на типи файлів (дозволити `*.*`) та додати опцію надання агенту доступу до інтернету для конкретної папки.

## 2. Функціональні вимоги (FR-A)
*   **FR-A1 (Multiple Workspaces):** Система повинна підтримувати одночасний моніторинг багатьох папок. У UI має бути список (DataGrid/ListView) вже доданих активних правил під формою створення.
*   **FR-A2 (Internet Access Toggle):** У форму додавання правила потрібно додати перемикач "Allow Internet Access". Якщо увімкнено, агент під час роботи з цією папкою зможе викликати `WebSearchTool` (трафік піде через BlackCat Bridge).
*   **FR-A3 (All Files Support):** Поле `Allowed Extensions` повинно за замовчуванням приймати значення `*.*` (всі файли). Агент повинен намагатися прочитати будь-який новий файл, використовуючи відповідні парсери (якщо це зображення — через Vision-модель, якщо бінарник — ігнорувати або читати метадані).
*   **FR-A4 (Rule Management):** Можливість видалити правило (зупинити моніторинг папки) без перезапуску сервісу.

---

## 3. ДЕКОМПОЗИЦІЯ ТА ПОКРОКОВІ ТАСКИ (ІНСТРУКЦІЯ ДЛЯ CLAUDE)

**@Claude:** Це інструкція для реалізації керування правилами автономного агента. Виконуй кроки послідовно.

### Step 1: Оновлення DTO (AsmodayCat.Shared)
1. Створи або онови `AgentRuleDto`:
   * `Guid Id` (Унікальний ідентифікатор правила)
   * `string InputPath`
   * `string OutputPath`
   * `string ActionType`
   * `string SystemPrompt`
   * `string AllowedExtensions` (За замовчуванням `*.*`)
   * `bool AllowInternetAccess` (Нове поле)
   * `bool IsActive` (Статус моніторингу)

### Step 2: Логіка Моніторингу (AsmodayCat.Agent & AsmodayCat.Service)
1. В `AsmodayCat.Agent/Watchers` реалізуй `WorkspaceManager`, який зберігає колекцію активних `FileSystemWatcher` (по одному на кожне правило).
2. Коли спрацьовує подія появи файлу, `PipelineProcessor` повинен перевіряти поле `AllowInternetAccess` з конфіга. Якщо воно `true`, в системний промпт для LLM додається дозвіл на використання інструменту пошуку в інтернеті.
3. В `AsmodayCat.Service/IpcServer` додай обробники команд:
   * `GetAgentRules` (повертає список всіх `AgentRuleDto`).
   * `AddAgentRule` (зберігає правило локально, наприклад в SQLite або JSON, і запускає Watcher).
   * `RemoveAgentRule` (зупиняє Watcher і видаляє конфіг).

### Step 3: ViewModel (AsmodayCat.UI)
1. Відкрий `AgentRulesViewModel`.
2. Додай `ObservableCollection<AgentRuleDto> ActiveRules`.
3. Додай `bool AllowInternetAccess` як `ObservableProperty` для біндингу з новим чекбоксом/світчем у формі.
4. Зміни значення за замовчуванням для `AllowedExtensions` на `*.*`.
5. Реалізуй команди `LoadRulesCommand`, `AddRuleCommand`, `RemoveRuleCommand(Guid id)`. Після додавання правила, колекція `ActiveRules` має оновитися.

### Step 4: Верстка XAML (AsmodayCat.UI)
1. Відкрий `AgentRulesView.xaml`.
2. У формі "Add Watched Folder" (під полем розширень) додай `ToggleButton` або `CheckBox` з текстом "Allow Internet Access for this agent".
3. **Нижче форми створення** додай новий розділ "Active Workspaces". Розмісти там `DataGrid` або `ItemsControl` (використай стиль матеріальних карток).
4. У списку активних правил виводь: `InputPath`, `ActionType`, іконки статусу інтернету (глобус) та іконку кошика (`RemoveRuleCommand`) для зупинки моніторингу.