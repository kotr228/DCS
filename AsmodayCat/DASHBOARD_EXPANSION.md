# ASMODAYCAT - DASHBOARD EXPANSION PLAN (PHASE 6.1)

## 1. Огляд завдання
Поточний `DashboardView` має лише базові метрики (CPU Load, RAM Free, Service Status) та кнопку "Kill Switch". Необхідно розширити дашборд, перетворивши його на повноцінний центр моніторингу ШІ-оркестратора. 

Дашборд повинен відображати стан локального "заліза" (особливо GPU/VRAM), активність завантажених моделей, статус розподіленої мережі (CoffeeCat) та останні дії автономного агента.

## 2. Функціональні вимоги до Дашборду (FR)
*   **FR-D1:** Додати віджети моніторингу GPU (Load %) та VRAM (Free / Used).
*   **FR-D2:** Додати віджет "Active LLM", який показує, яка модель зараз завантажена в пам'ять (наприклад, `qwen2.5-coder:7b`), її статус (Idle, Generating, Pulling) та кнопку швидкого вивантаження (Unload).
*   **FR-D3:** Додати віджет "Network Status", який показує кількість доступних вузлів AsmodayCat у локальній мережі (через CoffeeCat) для розподілених обчислень.
*   **FR-D4:** Додати блок "Recent Agent Activity" — міні-список (List) останніх 3-5 дій автономного агента (наприклад: "Обробка файлу report.pdf... Зроблено").
*   **FR-D5:** Оновлення даних на дашборді повинно відбуватися автоматично кожні 2-3 секунди без необхідності натискати кнопку "Refresh" вручну (кнопка може залишитись для примусового опитування).

---

## 3. ДЕКОМПОЗИЦІЯ ТА ПОКРОКОВІ ТАСКИ (ІНСТРУКЦІЯ ДЛЯ CLAUDE)

**@Claude:** Це твоя інструкція для розширення функціоналу дашборду. Виконуй кроки послідовно.

### Step 1: Оновлення DTO моделей (AsmodayCat.Shared)
1. Відкрий або створи існуючі DTO для моніторингу (наприклад, `SystemStatusDto`).
2. Додай властивості для GPU: `GpuLoadPercentage`, `VramUsedMegabytes`, `VramTotalMegabytes`.
3. Додай властивості для LLM: `ActiveModelName`, `ActiveModelStatus`.
4. Додай властивості для Мережі: `AvailableDistributedNodesCount`.
5. Створи DTO `AgentActivityLogDto` (Timestamp, ActionDescription, Status).

### Step 2: Логіка збору метрик (AsmodayCat.Service & AsmodayCat.Core)
1. Онови методи збору статистики в `AsmodayCat.Core/ResourceMonitor`. Для GPU наразі можна використати заглушки (Mocks), які генерують випадкові дані, якщо прямий доступ до NVIDIA NVML / AMD ADL ще не реалізований.
2. В `AsmodayCat.Service` розшир обробник IPC-команд (наприклад, команду `GetDashboardStats`), щоб він формував і повертав оновлений `SystemStatusDto` з усіма новими даними.

### Step 3: Оновлення ViewModels (AsmodayCat.UI)
1. Відкрий `DashboardViewModel`.
2. Додай `ObservableProperty` для всіх нових метрик (GPU Load, VRAM, Active Model, Nodes Count).
3. Додай `ObservableCollection<AgentActivityLogDto>` для списку останніх дій.
4. Налаштуй `DispatcherTimer` (або аналог у фоновому потоці), який автоматично відправляє IPC-запит `GetDashboardStats` кожні 2 секунди та оновлює властивості.
5. Створи `RelayCommand` для нової дії: `UnloadActiveModelCommand`.

### Step 4: Верстка XAML (AsmodayCat.UI)
1. Відкрий `DashboardView.xaml`.
2. Перебудуй існуючу сітку (Grid) або використовуй `WrapPanel`/`UniformGrid`, щоб розмістити нові картки-віджети.
   * *Верхній ряд:* Статус Сервісу, Active Model (з кнопкою Unload).
   * *Середній ряд (Hardware):* CPU Load, RAM, GPU Load, VRAM.
   * *Нижній ряд:* Доступні ноди мережі та панель "Recent Agent Activity" (через `ItemsControl` або `ListView`).
3. Використовуй стилістику `MaterialDesignThemes` (наприклад, картки `materialDesign:Card`, іконки `materialDesign:PackIcon`). Кнопки небезпечних дій (Kill Switch, Unload) виділяй акцентним кольором (наприклад, Secondary/Error).

# ASMODAYCAT - DASHBOARD CHARTS (PHASE 6.2)

## 1. Огляд завдання
Замість статичних текстових значень навантаження, `DashboardView` повинен відображати динамічні графіки в реальному часі. Оскільки в екосистемі (у проєкті BlackCat) вже використовується підхід з графіками (наприклад, `TrafficChartViewModel` та `TrafficDataPoint`[cite: 1]), AsmodayCat має наслідувати цей архітектурний патерн для метрик апаратного забезпечення.

## 2. Функціональні вимоги до графіків (FR)
*   **FR-C1:** Створити спільний графік "System Load (%)", де двома різними лініями (наприклад, фіолетовою та червоною) відображатиметься завантаження CPU та GPU за останні 60 секунд.
*   **FR-C2:** Створити графік "Memory Usage (MB/GB)", де відображатиметься використання RAM та VRAM.
*   **FR-C3 (Опціонально):** Графік "LLM Speed", який показує кількість згенерованих токенів за секунду (Tokens/sec) під час активної роботи моделі.

---

## 3. ДЕКОМПОЗИЦІЯ ТА ПОКРОКОВІ ТАСКИ (ІНСТРУКЦІЯ ДЛЯ CLAUDE)

**@Claude:** Виконай наступні кроки для реалізації графіків реального часу. Перевір, яка бібліотека графіків використовується в інших проєктах CatSuite (якщо не знайдеш — встанови `LiveChartsCore.SkiaSharpView.WPF` як найсучасніший варіант для MVVM).

### Step 1: Моделі для історії даних (AsmodayCat.Shared)
1. За аналогією з `TrafficDataPoint` з BlackCat[cite: 1], створи модель `ResourceDataPoint` у `AsmodayCat.Shared/Models`. Вона повинна містити `Timestamp` та значення (Value).
2. Онови `SystemStatusDto`, щоб він містив не лише поточні значення, а й масиви/списки останніх точок даних для графіків (наприклад, `List<ResourceDataPoint> CpuHistory`).

### Step 2: Збір історії у фоні (AsmodayCat.Service)
1. У фоновому сервісі (у `ResourceMonitor` або `AsmodayWorker`) додай кільцеві буфери (Circular Buffers) або просто `Queue<ResourceDataPoint>` з лімітом у 60 записів (для зберігання історії за 1 хвилину).
2. Коли UI запитує статуси через IPC, сервіс має віддавати ці масиви історичних даних.

### Step 3: ViewModels для графіків (AsmodayCat.UI)
1. За аналогією з `TrafficChartViewModel`[cite: 1], створи `HardwareChartViewModel` в `AsmodayCat.UI/ViewModels`.
2. Налаштуй серії даних (Series) для графіків (наприклад, `ISeries[]` для LiveCharts).
3. Налаштуй осі X (час) та Y (відсотки від 0 до 100).
4. Додай логіку оновлення колекцій `ObservableCollection`, коли надходять нові дані від `AsmodayCat.Service`.

### Step 4: Верстка графіків (AsmodayCat.UI)
1. Відкрий `DashboardView.xaml`.
2. Заміни текстові блоки "CPU LOAD" на елементи керування графіками (наприклад, `<lvc:CartesianChart Series="{Binding HardwareChartViewModel.CpuGpuSeries}" />`).
3. Використай акцентні кольори з існуючої теми WPF для стилізації ліній графіку (наприклад, плавні криві з напівпрозорою заливкою знизу — Area Series).