# ASMODAYCAT - DATABASE & PERSISTENCE LAYER (PHASE 12)

## 1. Огляд завдання
До цього моменту дані (правила агентів, історія чату, налаштування "заліза") зберігалися в пам'яті або за допомогою заглушок (Mocks). Необхідно реалізувати повноцінний рівень збереження даних (Persistence Layer) на базі **SQLite**. 
За архітектурний еталон беремо реалізацію з проєкту **BlackCat** (див. `BlackCat.Core/Data`). Нам потрібні мігратор, схема БД та репозиторії для кожної сутності.

## 2. Нормалізована схема БД (Словник даних)

База даних `asmodaycat.db` складатиметься з 5 основних таблиць:

**1. Таблиця `HardwareSettings` (Апаратні конфігурації та ліміти)**
Оскільки налаштування "заліза" є критичними, вони зберігаються у строго типізованій таблиці. У ній завжди буде лише один запис (наприклад, з `Id = 1`), який оновлюється.
* `Id` (INTEGER, Primary Key) - Завжди дорівнює 1 (синглтон-запис).
* `PreferredDevice` (TEXT, Not Null) - Значення: 'Auto', 'GPU', 'CPU'.
* `ContextWindowSize` (INTEGER, Not Null) - Наприклад: 4096, 8192.
* `MaxVramAllocationMb` (INTEGER, Not Null) - Ліміт пам'яті (0 = безліміт).
* `CpuThreads` (INTEGER, Not Null) - Кількість виділених ядер (наприклад: 4, 8).
* `AllowCpuFallback` (INTEGER, Not Null) - 0 (False) або 1 (True).
* `UpdatedAt` (DATETIME)

**2. Таблиця `AppSettings` (Ключ-Значення для глобальних та апаратних налаштувань)**
* `Key` (TEXT, Primary Key) - Наприклад: 'Hardware.CpuThreads', 'Hardware.PreferredDevice'
* `Value` (TEXT)
* `UpdatedAt` (DATETIME)

**3. Таблиця `AgentWorkspaces` (Правила моніторингу папок)**
* `Id` (TEXT, Primary Key, GUID)
* `InputPath` (TEXT, Not Null)
* `OutputPath` (TEXT, Not Null)
* `ActionType` (TEXT, Not Null)
* `SystemPrompt` (TEXT, Not Null)
* `AllowedExtensions` (TEXT, Not Null) - Наприклад: '*.*'
* `AllowInternetAccess` (INTEGER, 0/1)
* `IsActive` (INTEGER, 0/1)
* `CreatedAt` (DATETIME)

**4. Таблиця `ChatSessions` (Історія сесій з ШІ-агентом)**
* `Id` (TEXT, Primary Key, GUID)
* `Title` (TEXT, Not Null) - Згенерується ШІ на основі першого повідомлення
* `CreatedAt` (DATETIME)
* `UpdatedAt` (DATETIME)

**5. Таблиця `ChatMessages` (Повідомлення всередині сесії)**
* `Id` (TEXT, Primary Key, GUID)
* `SessionId` (TEXT, Foreign Key -> ChatSessions.Id, ON DELETE CASCADE)
* `Role` (TEXT, Not Null) - 'User', 'Assistant', 'System'
* `Content` (TEXT, Not Null)
* `Timestamp` (DATETIME)
* `AttachmentsJson` (TEXT) - Зберігає масив шляхів до прикріплених файлів `["C:\img.png"]`

**6. Таблиця `KnownModels` (Локальний пул моделей)**
* `Id` (TEXT, Primary Key, GUID)
* `Name` (TEXT, Unique) - Наприклад: 'qwen2.5-coder:7b'
* `RecommendedTask` (TEXT)
* `IsCustom` (INTEGER, 0/1) - 0 для базової матриці, 1 для доданих вручну

---

## 3. ДЕКОМПОЗИЦІЯ ТА ПОКРОКОВІ ТАСКИ (ІНСТРУКЦІЯ ДЛЯ CLAUDE)

**@Claude:** Це інструкція для імплементації рівня бази даних. За приклад архітектури бери `BlackCat.Core/Data`. Виконуй кроки послідовно.

### Step 1: Ядро Бази Даних (`AsmodayCat.Core/Data`)
1. Встанови NuGet пакет `Microsoft.Data.Sqlite` у проєкт `AsmodayCat.Core`.
2. Створи клас `DatabaseSchema`. Напиши в ньому SQL-запити (DDL) для створення 5 таблиць, описаних вище, з урахуванням Foreign Keys.
3. Створи клас `DatabaseMigrator`, який перевіряє наявність БД `asmodaycat.db` у `AppData/Local/CatSuite/AsmodayCat` (або в папці поруч з exe) і виконує скрипти з `DatabaseSchema`, якщо таблиці відсутні.
4. Створи базовий клас `AsmodayDatabase` для отримання підключення `SqliteConnection`.

### Step 2: Патерн Репозиторіїв (`AsmodayCat.Core/Data/Repositories`)
Створи класи репозиторіїв, використовуючи сирий SQL (через `SqliteCommand`) або Dapper (якщо він є в залежностях):
1. **`HardwareSettingsRepository`**: 
   * Метод `GetConfig()` -> робить `SELECT * FROM HardwareSettings WHERE Id = 1`. Якщо запису немає — створює дефолтний `INSERT` і повертає його.
   * Метод `SaveConfig(HardwareConfigDto dto)` -> робить `UPDATE HardwareSettings SET ... WHERE Id = 1`.
2. **`SettingsRepository`**: Методи `GetSetting(string key)`, `SetSetting(string key, string value)`, `GetHardwareConfig()`, `SaveHardwareConfig(HardwareConfigDto dto)`.
3. **`AgentWorkspaceRepository`**: Методи `GetAll()`, `GetActive()`, `Add(AgentRuleDto rule)`, `Update(AgentRuleDto rule)`, `Delete(Guid id)`.
4. **`ChatRepository`**: 
   * `CreateSession(string title)`
   * `GetSessions()`
   * `GetMessagesBySession(Guid sessionId)`
   * `AddMessage(ChatMessageDto message)`
5. **`ModelPoolRepository`**: Методи `GetKnownModels()`, `AddCustomModel(string name)`.

### Step 3: Інтеграція Сервісу (`AsmodayCat.Service`)
1. Відкрий `Program.cs` в `AsmodayCat.Service`.
2. Зареєструй `DatabaseMigrator`, `SettingsRepository`, `AgentWorkspaceRepository`, `ChatRepository` та `ModelPoolRepository` в DI-контейнері (`AddSingleton`).
3. В `AsmodayWorker` (або на етапі старту хоста) виклич `DatabaseMigrator.InitializeDatabase()`.
4. Знайди обробники IPC (які ти створив у попередніх фазах) і заміни збереження в пам'яті (in-memory lists) на виклики відповідних репозиторіїв.

### Step 4: Відновлення стану агента після перезапуску
1. В `AsmodayCat.Agent/WorkspaceManager` додай логіку, яка при старті сервісу звертається до `AgentWorkspaceRepository.GetActive()`.
2. Для кожного активного правила автоматично створюй `FileSystemWatcher` та запускай моніторинг директорій, щоб агенти одразу "ставали до роботи" після ввімкнення комп'ютера або перезапуску служби.