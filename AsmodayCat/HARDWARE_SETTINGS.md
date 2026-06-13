# ASMODAYCAT - HARDWARE & EXECUTION SETTINGS (PHASE 9)

## 1. Огляд завдання
Наразі управління апаратним забезпеченням базове. Необхідно розширити вікно налаштувань (Hardware Settings), щоб надати адміністратору гранулярний контроль над тим, як саме локальні LLM споживають ресурси системи. Це критично важливо для стабільної роботи у фоні, щоб агент не "з'їв" усю пам'ять під час важких задач з кодової бази.

## 2. Функціональні вимоги (FR-H)
* **FR-H1 (Device Selection):** Можливість жорстко прив'язати виконання до конкретного пристрою (GPU_0, CPU) або залишити Auto-Routing.
* **FR-H2 (Context Window Limit):** Повзунок (Slider) для налаштування розміру контексту (`num_ctx`) для моделей (наприклад, від 2048 до 32768 токенів). Чим більший контекст, тим більше VRAM потрібно для завантаження.
* **FR-H3 (VRAM Capping):** Можливість обмежити максимальний обсяг відеопам'яті, який дозволено використовувати моделі (наприклад, "Не більше 4 GB").
* **FR-H4 (CPU Fallback Optimization):** Налаштування кількості потоків (`num_thread`), які модель може використовувати при генерації на процесорі. Дозволяє оптимізувати роботу під сучасні багатоядерні CPU.
* **FR-H5 (Persistent Config):** Усі налаштування апаратного забезпечення повинні зберігатися локально (наприклад, у `appsettings.json` або SQLite) та автоматично застосовуватися після перезапуску `AsmodayCat.Service`.

---

## 3. ДЕКОМПОЗИЦІЯ ТА ПОКРОКОВІ ТАСКИ (ІНСТРУКЦІЯ ДЛЯ CLAUDE)

**@Claude:** Це інструкція для імплементації розширених апаратних налаштувань. Виконуй кроки послідовно.

### Step 1: DTO та Конфігурація (AsmodayCat.Shared)
1. Створи `HardwareConfigDto` з властивостями:
   * `string PreferredDevice` ("Auto", "GPU", "CPU")
   * `int ContextWindowSize` (за замовчуванням 4096)
   * `int MaxVramAllocationMb` (за замовчуванням 0 — без ліміту)
   * `int CpuThreads` (за замовчуванням 4)
   * `bool AllowCpuFallback` (true/false)

### Step 2: Логіка застосування конфігів (AsmodayCat.Core & AsmodayCat.Service)
1. В `AsmodayCat.Core/Engine` онови `OllamaClient` (або абстракцію рушія), щоб при викликах `/api/generate` або `/api/chat` в об'єкт `options` передавалися параметри з `HardwareConfigDto` (наприклад, `num_ctx`, `num_thread`).
2. В `AsmodayCat.Service` реалізуй збереження та завантаження цієї конфігурації. Додай IPC-команди:
   * `GetHardwareConfig`
   * `SaveHardwareConfig` (приймає DTO і зберігає на диск).

### Step 3: ViewModel (AsmodayCat.UI)
1. Відкрий або створи `HardwareSettingsViewModel`.
2. Реалізуй `ObservableProperty` для всіх налаштувань (ContextSize, Threads тощо), зв'язавши їх з відповідними властивостями DTO.
3. Додай команду `SaveConfigCommand`, яка відправляє оновлений стан через IPC-клієнт у фоновий сервіс.
4. Додай логіку валідації (наприклад, щоб кількість потоків CPU не перевищувала логічний максимум системи).

### Step 4: Верстка XAML (AsmodayCat.UI)
1. У відповідному View (`HardwareSettingsView.xaml`) розмісти елементи керування:
   * **Device Preference:** `ComboBox` (Auto, GPU Only, CPU Only).
   * **Context Size:** `Slider` (від 2048 до 32768, з кроком 1024) та `TextBlock` для відображення поточного значення. Додай Tooltip: *"Більший контекст споживає експоненціально більше VRAM"*.
   * **CPU Threads:** `Slider` (від 1 до 16/32).
   * **Fallback:** `ToggleButton` (Дозволити перемикання на CPU, якщо VRAM переповнена).
2. Використовуй картки `materialDesign:Card` для групування налаштувань (наприклад, група "GPU Settings" та "CPU Settings").
3. Розмісти знизу помітну кнопку "Save Parameters".