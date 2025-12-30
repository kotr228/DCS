# 🧪 CatSuite Installer - Інструкції для тестування

## 📋 Що було змінено (v2.0.0)

### ✅ Виправлено:

1. **Завантаження маніфесту:**
   - ❌ Раніше: Тільки Google Drive (жорстко зашито)
   - ✅ Тепер: Мульти-джерельна система з пріоритетами:
     1. Локальний файл `manifest.json` (поруч з exe)
     2. HTTP сервер `http://localhost:8080/manifest.json`
     3. Production сервер (налаштовується)
     4. Google Drive (fallback для сумісності)

2. **Алгоритм оновлення:**
   - ❌ Раніше: Завантаження ZIP архіву + розпаковка + складна логіка з Google Drive confirm
   - ✅ Тепер: Пряме завантаження DLL файлу + простий батник для заміни

3. **Надійність:**
   - ✅ Автоматичні ретраї (3 спроби) при завантаженні
   - ✅ Прогрес-бар завантаження (KB/MB, відсотки)
   - ✅ SHA256 перевірка для безпеки
   - ✅ Детальне логування всіх етапів

---

## 🚀 Швидкий старт (Локальне тестування)

### Варіант 1: З локальним файлом (БЕЗ сервера)

1. **Створіть manifest.json поруч з CatSuite.Launcher.exe:**
   ```
   CatSuite.Launcher.exe
   manifest.json          ← Створити тут
   CatSuite.Installer.dll
   ```

2. **Скопіюйте зразок:**
   ```bash
   copy TestServer\www\manifest.json .
   ```

3. **Запустіть:**
   ```bash
   CatSuite.Launcher.exe
   ```

4. **Очікуваний результат:**
   ```
   === CatSuite Launcher v2.0.0 ===
   📂 Робочий каталог: C:\Your\Path

   🔍 Перевірка локального файлу...
   ✅ Маніфест завантажено з локального файлу

   🔍 Локальна версія ядра: 1.0.0
   🌐 Версія ядра в маніфесті: 1.0.0
   ✅ Ядро актуальне. Запускаємо основний інсталятор...
   ```

---

### Варіант 2: З HTTP сервером (ПОВНЕ тестування)

#### Крок 1: Запуск тестового HTTP сервера

1. **Запустіть TestServer:**
   ```bash
   cd TestServer
   dotnet run
   ```

   Або скомпільований:
   ```bash
   CatSuite.TestServer.exe
   ```

2. **Має з'явитися:**
   ```
   ═══════════════════════════════════════════════
     CatSuite Test HTTP Server
   ═══════════════════════════════════════════════

   📂 Web Root: C:\Path\TestServer\www
   🌐 Listening on:
      http://localhost:8080/
      http://127.0.0.1:8080/

   Натисніть Ctrl+C для зупинки...
   ═══════════════════════════════════════════════
   ```

3. **Перевірте в браузері:**
   ```
   http://localhost:8080/manifest.json
   ```

   Має показати JSON з пакетами.

#### Крок 2: Підготовка файлів для роздачі

Створіть структуру в `TestServer/www/`:

```
TestServer/www/
├── manifest.json                          ← Вже є
├── installer/
│   └── CatSuite.Installer.dll             ← Скопіюйте з bin/Release
└── packages/
    ├── DocControlService.msi              ← Ваші MSI пакети
    ├── DocControlUI.msi
    └── CoffeeCat.Samples.msi
```

**Команди:**
```bash
cd TestServer\www
mkdir installer
mkdir packages

# Скопіюйте DLL ядра інсталятора
copy ..\..\CatSuite.Installer\bin\Release\net8.0-windows\CatSuite.Installer.dll installer\

# Скопіюйте MSI пакети (якщо є)
copy C:\YourMsiPackages\*.msi packages\
```

#### Крок 3: Запуск Launcher

1. **Видаліть локальний manifest.json** (якщо є):
   ```bash
   del manifest.json
   ```

2. **Запустіть Launcher:**
   ```bash
   CatSuite.Launcher.exe
   ```

3. **Очікуваний результат:**
   ```
   === CatSuite Launcher v2.0.0 ===

   🔍 Перевірка локального файлу...
      ⚠️ Помилка читання файлу: Could not find file...

   🔍 Перевірка локального HTTP сервера...
   ✅ Маніфест завантажено з локального сервера

   🔍 Локальна версія ядра: 1.0.0
   🌐 Версія ядра в маніфесті: 1.0.0
   ✅ Ядро актуальне. Запускаємо основний інсталятор...
   ```

4. **У логах HTTP сервера має з'явитися:**
   ```
   [12:34:56] GET /manifest.json
      ✅ 200 OK (1234 bytes, application/json)
   ```

---

## 🧪 Тестування самооновлення інсталятора

### Сценарій: Нова версія ядра доступна

1. **Змініть версію в manifest.json:**
   ```json
   {
     "InstallerCore": {
       "Version": "2.0.0",  ← Збільште версію
       "Url": "http://localhost:8080/installer/CatSuite.Installer.dll",
       "Sha256": ""
     }
   }
   ```

2. **Запустіть Launcher:**
   ```bash
   CatSuite.Launcher.exe
   ```

3. **Очікуваний результат:**
   ```
   🔍 Локальна версія ядра: 1.0.0
   🌐 Версія ядра в маніфесті: 2.0.0
   ⚡ Потрібне самооновлення інсталятора!

   ⬇️ Завантаження оновлення...
      URL: http://localhost:8080/installer/CatSuite.Installer.dll

      Спроба 1/3...
      Прогрес: 100% (512 KB / 512 KB)
      ✅ Завантажено: 0.5 MB

   🚀 Запуск оновлення...
      (Лаунчер перезапуститься автоматично)
   ```

4. **З'явиться батник:**
   ```
   === CatSuite Updater v2.0 ===
   Оновлення інсталятора...

   Оновлення успішне!
   ```

5. **Launcher перезапуститься** і покаже:
   ```
   🔍 Локальна версія ядра: 2.0.0
   🌐 Версія ядра в маніфесті: 2.0.0
   ✅ Ядро актуальне.
   ```

---

## 🧪 Тестування встановлення пакетів

1. **Запустіть інсталятор** (після успішного запуску Launcher):
   - Має відкритися вікно з списком пакетів
   - Пакети відображаються з категоріями (Core, Dependencies, Optional)

2. **Оберіть пакет для встановлення:**
   - Поставте галочку біля "DocControl Windows Service"
   - Натисніть "Встановити"

3. **Очікувана поведінка:**
   - Показ плану встановлення (з урахуванням залежностей)
   - Завантаження MSI з `http://localhost:8080/packages/...`
   - Прогрес-бар завантаження
   - Виконання `msiexec /i package.msi /qn ...`
   - Логування в БД

4. **Перевірка логів:**
   - База даних: `%LocalAppData%\CatSuite\installer.db`
   - MSI логи: `%LocalAppData%\CatSuite\Logs\`
   - Кеш файлів: `%LocalAppData%\CatSuite\Cache\`

---

## 🔧 Налаштування

### Зміна джерела маніфесту

Відредагуйте `CatSuite.Launcher/Program.cs`:

```csharp
// Рядки 23-25
private const string LOCAL_MANIFEST_PATH = "manifest.json";
private const string HTTP_SERVER_URL = "http://localhost:8080/manifest.json";
private const string PRODUCTION_SERVER_URL = "https://your-server.com/catsuite/manifest.json";
```

### Зміна порту HTTP сервера

```bash
CatSuite.TestServer.exe 9090
```

Або в `SimpleHttpServer.cs`:
```csharp
public SimpleHttpServer(int port = 9090, ...)  // Змінити 8080 на 9090
```

### Обчислення SHA256 хешу

Для безпеки рекомендується додати SHA256 хеш файлів:

```powershell
# PowerShell
Get-FileHash CatSuite.Installer.dll -Algorithm SHA256

# Результат:
# Algorithm       Hash
# ---------       ----
# SHA256          A1B2C3D4E5...
```

Додайте в manifest.json:
```json
{
  "InstallerCore": {
    "Version": "1.0.0",
    "Url": "...",
    "Sha256": "A1B2C3D4E5..."  ← Вставити сюди (lowercase)
  }
}
```

---

## 🐛 Troubleshooting

### Помилка: "Не вдалося завантажити маніфест з жодного джерела"

**Причини:**
1. Локальний файл відсутній
2. HTTP сервер не запущений
3. Production сервер недоступний

**Рішення:**
```bash
# Перевірте чи є manifest.json
dir manifest.json

# Перевірте чи працює HTTP сервер
curl http://localhost:8080/manifest.json

# Запустіть сервер якщо потрібно
cd TestServer
dotnet run
```

---

### Помилка: "Access denied" при запуску HTTP сервера

**Причина:** Windows вимагає прав адміністратора для HTTP.sys

**Рішення 1:** Запустіть від адміністратора:
```bash
# Правий клік → "Запустити від імені адміністратора"
CatSuite.TestServer.exe
```

**Рішення 2:** Використайте інший порт (8888, 9000):
```bash
CatSuite.TestServer.exe 8888
```

**Рішення 3:** Дайте дозвіл на порт:
```bash
netsh http add urlacl url=http://+:8080/ user=DOMAIN\username
```

---

### Помилка: "404 Not Found" при завантаженні пакетів

**Причина:** Файли відсутні в `TestServer/www/packages/`

**Рішення:**
```bash
# Перевірте структуру
dir TestServer\www\packages

# Скопіюйте MSI файли
copy YourPackages\*.msi TestServer\www\packages\
```

---

### Помилка: "Контрольна сума не співпадає"

**Причина:** SHA256 в manifest.json не відповідає файлу

**Рішення:**
```powershell
# Обчисліть хеш файлу
Get-FileHash TestServer\www\installer\CatSuite.Installer.dll

# Оновіть manifest.json з правильним хешем
# АБО видаліть рядок "Sha256" для пропуску перевірки
```

---

## 📊 Структура проекту після змін

```
CatSuite.Installer/
├── CatSuite.Launcher/
│   └── Program.cs                 ← ПЕРЕРОБЛЕНO v2.0
│       - Мульти-джерельне завантаження
│       - Спрощене оновлення (без ZIP)
│       - Ретраї та прогрес
│
├── CatSuite.Installer/            ← Без змін
│   └── (Ядро інсталятора)
│
├── TestServer/                    ← НОВИЙ проект
│   ├── SimpleHttpServer.cs        ← HTTP сервер для тестування
│   ├── CatSuite.TestServer.csproj
│   └── www/
│       ├── manifest.json          ← Приклад конфігурації
│       ├── installer/
│       │   └── CatSuite.Installer.dll
│       └── packages/
│           └── *.msi
│
└── README_TESTING.md              ← Цей файл
```

---

## ✅ Checklist для тестування

- [ ] Launcher запускається без помилок
- [ ] Локальний файл manifest.json завантажується
- [ ] HTTP сервер запускається на localhost:8080
- [ ] HTTP сервер віддає manifest.json
- [ ] Launcher завантажує маніфест з HTTP сервера
- [ ] Самооновлення інсталятора працює (зміна версії)
- [ ] Завантаження файлів з прогрес-баром
- [ ] SHA256 перевірка працює
- [ ] Батник оновлення виконується
- [ ] Launcher перезапускається після оновлення
- [ ] Інсталятор відкривається з списком пакетів
- [ ] MSI пакети завантажуються
- [ ] MSI встановлюється через msiexec
- [ ] Логи записуються в БД

---

## 📞 Підтримка

Якщо виникають проблеми:
1. Перевірте логи у консолі
2. Перевірте структуру файлів
3. Запустіть від адміністратора
4. Перевірте брандмауер Windows

**Версія:** v2.0.0
**Дата:** 2024-12-30
