ASMODAYCAT - MASTER DEVELOPMENT PLAN & ARCHITECTURE
1. Огляд проєкту та його роль в екосистемі
AsmodayCat — це інтелектуальне ядро екосистеми CatSuite. Це фоновий сервіс та графічний інтерфейс для оркестрації великих мовних моделей (LLM), локальних автономних агентів та розподілених обчислень.

Взаємодія з екосистемою:

CoffeeCat (DocControlSystem): Використовується для роботи в локальній мережі. AsmodayCat використовує мережеве ядро CoffeeCat (DocControlNetworkCore) для пошуку інших вузлів та розподілу (балансування) важких ШІ-задач між пристроями в локальній мережі. Також CoffeeCat синхронізує файли, з якими працює агент AsmodayCat.

BlackCat: Використовується для роботи з інтернетом та безпекою. AsmodayCat використовує BlackCat для безпечного завантаження моделей, доступу до зовнішніх API (як fallback, якщо локальна LLM не справляється), а також для створення захищених тунелів дистанційного керування агентом ззовні.

2. Високорівнева архітектура (Solution Structure)
Проєкт базується на .NET 8.0 і складається з 6 модулів:

AsmodayCat.Shared (Class Library): Контракти (інтерфейси), DTO, Enums. Жодної бізнес-логіки.

AsmodayCat.Core (Class Library): Двигун ШІ. Управління LLM, вибір апаратного забезпечення (CPU/GPU), промптинг.

AsmodayCat.Network (Class Library): Мости (Bridges) до CoffeeCat та BlackCat. Логіка балансування навантаження.

AsmodayCat.Agent (Class Library): Моніторинг папок ("пісочниць"), пайплайни обробки файлів (Читання -> Аналіз LLM -> Збереження результату).

AsmodayCat.Service (Worker Service): Головний фоновий Windows-демон. Тримає всі модулі, має IPC-сервер (Named Pipes).

AsmodayCat.UI (WPF Application): Темна/монохромна панель керування. Спілкується з Service через IPC-клієнт. Використовує CommunityToolkit.Mvvm.

3. Бізнес-логіка (Основні потоки)
Hardware Execution (Вибір "Заліза"): Модуль HardwareSelector сканує систему на наявність GPU (CUDA/DirectML) та CPU. Користувач у UI або сам агент (на основі навантаження) вибирає, де саме розгортати модель.

Load Balancing (CoffeeCat Bridge): Коли надходить великий батч завдань (наприклад, аналіз 100 документів), AsmodayCat запитує через CoffeeCat статуси інших комп'ютерів у мережі. Завдання розбиваються на чанки і відправляються вільним машинам.

Agentic Workspace: Адміністратор прив'язує AsmodayCat до папки (наприклад, C:\CatSuite\Tasks). Агент отримує повні права. При появі нового файлу агент його захоплює, обробляє (згідно з системним промптом для цієї папки) і створює артефакт-відповідь.

4. ДЕКОМПОЗИЦІЯ ТА ПОКРОКОВІ ТАСКИ (ІНСТРУКЦІЯ ДЛЯ CLAUDE)
@Claude: Це твій покроковий план. ПРАВИЛО №1: Виконуй строго одну фазу за раз. Не переходь до наступної фази, поки поточна не буде ідеально реалізована, протестована (компіляцією) і затверджена користувачем. Використовуй заглушки (Mocks) для зовнішніх сервісів, які ще не імплементовані.

Phase 1: Базовий фундамент (AsmodayCat.Shared)
Створити структуру папок: Models, Enums, Interfaces.

Enums: * ExecutionDevice (CPU, GPU_Nvidia, GPU_AMD).

TaskPriority (Low, Normal, High).

AgentStatus (Idle, Analyzing, Generating, Error).

NetworkTaskState (Local, Distributed, Completed, Failed).

Models (DTOs): * LlmRequest (Prompt, Context, RequiredVram, ExecutionDevice).

LlmResponse (Content, TokensPerSecond, UsedDevice).

NodeResourceStatus (CpuLoad, RamFree, GpuLoad, VramFree).

AgentFolderConfig (Path, SystemPrompt, FileExtensionsAllowed).

Interfaces (Контракти):

ILLMEngine (Методи: LoadModelAsync, GenerateAsync, UnloadAsync).

IHardwareScanner (Метод: GetAvailableDevices).

IAgentController (Методи: StartWatching, StopWatching).

IDistributedRouter (Метод: RouteTaskAsync).

Phase 2: Інтелектуальне Ядро (AsmodayCat.Core)
Hardware Management (/Hardware):

Реалізувати HardwareScanner (використовуючи System.Management для базового отримання інфо про GPU/CPU або заглушки для початку).

Реалізувати ResourceManager для відстеження поточного споживання пам'яті моделями.

LLM Engine (/Engine):

Створити базовий провайдер на основі локального API (наприклад, клієнт для Ollama REST API або абстракцію для llama.cpp).

Реалізувати логіку застосування ExecutionDevice (передача правильних параметрів у LLM бекенд).

Prompting (/Prompts):

Створити PromptBuilder для форматування контексту.

Phase 3: Автономний Агент (AsmodayCat.Agent)
Workspace Watcher (/Watchers):

Реалізувати FolderObserver на базі FileSystemWatcher. Він має ставити події створення/зміни файлів у потокобезпечну чергу (Channel або ConcurrentQueue).

Pipeline Processor (/Pipelines):

Реалізувати worker, який бере файл з черги, читає його, звертається до AsmodayCat.Core (через ILLMEngine) з системним промптом папки, отримує результат і записує його у вихідний файл.

Access Manager:

Логіка базової перевірки прав доступу до директорій.

Phase 4: Мережеві Мости (AsmodayCat.Network)
Оскільки ми інтегруємося з існуючим кодом CatSuite, тут створюємо адаптери.

CoffeeCat Bridge (/CoffeeCat):

Реалізувати NetworkTaskDistributor. Логіка: якщо завдання завелике, серіалізувати LlmRequest, імітувати відправку через P2P-протокол (створити інтерфейси-заглушки для DocControlNetworkCore), чекати асинхронну відповідь.

Реалізувати збирач статусів NodeResourceStatus з інших комп'ютерів.

BlackCat Bridge (/BlackCat):

Реалізувати перевірку дозволів на вихід в інтернет. Якщо локальна модель падає, формуємо запит до fallback API (наприклад, OpenAI/Claude), але пропускаємо його через логічний фільтр BlackCat (Mock-інтерфейс перевірки).

Phase 5: Оркестратор та Фоновий Демон (AsmodayCat.Service)
Worker Service Setup (Worker.cs):

Налаштувати Microsoft.Extensions.Hosting.WindowsServices.

Зареєструвати всі сервіси з Shared, Core, Agent, Network через Dependency Injection (DI) у Program.cs.

IPC Server (/Ipc):

Реалізувати NamedPipeServerStream (або локальний gRPC) для прийняття команд від UI.

Додати обробники команд: GetStatus, StartAgent, StopAgent, ChangeHardwareModel, GetSystemLoad.

Головний цикл:

У ExecuteAsync додати періодичний лог пульсу системи та очищення моделей (Idle Unload), якщо вони не використовуються довго.

Phase 6: WPF Панель Керування (AsmodayCat.UI)
Стилістика: Темна, строга (Dark Fantasy / Military-Tech).

Setup:

Встановити CommunityToolkit.Mvvm та MaterialDesignThemes.

Налаштувати DI для UI (щоб ViewModels отримували сервіси).

IPC Client (/Services):

Реалізувати клієнт для підключення до Named Pipe AsmodayCat.Service.

ViewModels & Views:

DashboardViewModel: Віджети загального навантаження, статус CoffeeCat-з'єднань, активна модель.

HardwareViewModel: Випадаючі списки для жорсткого призначення пристроїв (CPU/GPU) для конкретних задач.

AgentRulesViewModel: Інтерфейс додавання папок, налаштування розширень файлів та системних промптів для кожної папки.

TerminalViewModel: Вікно живого логування дій агента та сервісу.