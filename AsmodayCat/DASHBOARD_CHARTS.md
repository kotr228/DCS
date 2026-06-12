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