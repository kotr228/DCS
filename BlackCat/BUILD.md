# Збірка та розгортання BlackCat Firewall

## Вимоги до розробки

### Обов'язкові

- **Windows 10/11** (x64)
- **.NET 8.0 SDK** ([завантажити](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Visual Studio 2022** (рекомендовано) або **Visual Studio Code**
- **Git** для версійного контролю

### Рекомендовані

- **Windows Terminal** для зручної роботи з командним рядком
- **WinPcap** або **Npcap** для повного перехоплення пакетів
- **SQL Browser** для перегляду SQLite бази даних

---

## Швидкий старт

```bash
# Клонувати репозиторій
git clone https://github.com/kotr228/DCS.git
cd DCS/BlackCat

# Відновити NuGet пакети
dotnet restore

# Зібрати всі проекти
dotnet build --configuration Release

# Запустити UI (потребує прав адміністратора)
cd BlackCat.UI
dotnet run
```

---

## Детальна збірка

### 1. Підготовка середовища

#### Встановлення .NET 8.0 SDK

```powershell
# Перевірити чи встановлено .NET 8.0
dotnet --list-sdks

# Якщо немає, завантажити з:
# https://dotnet.microsoft.com/download/dotnet/8.0
```

#### Встановлення Visual Studio 2022

**Workloads для встановлення:**
- .NET Desktop Development
- Windows Presentation Foundation (WPF)

### 2. Відновлення залежностей

```bash
# З кореневої директорії BlackCat/
dotnet restore BlackCat.sln

# Або для окремих проектів:
dotnet restore BlackCat.Shared/BlackCat.Shared.csproj
dotnet restore BlackCat.Crypto/BlackCat.Crypto.csproj
dotnet restore BlackCat.NetworkCore/BlackCat.NetworkCore.csproj
dotnet restore BlackCat.Core/BlackCat.Core.csproj
dotnet restore BlackCat.Service/BlackCat.Service.csproj
dotnet restore BlackCat.UI/BlackCat.UI.csproj
```

### 3. Збірка проектів

#### Debug збірка

```bash
dotnet build BlackCat.sln --configuration Debug
```

#### Release збірка

```bash
dotnet build BlackCat.sln --configuration Release
```

#### Збірка окремих проектів

```bash
# BlackCat.UI
dotnet build BlackCat.UI/BlackCat.UI.csproj -c Release

# BlackCat.Service
dotnet build BlackCat.Service/BlackCat.Service.csproj -c Release
```

### 4. Публікація (для розгортання)

#### Self-contained публікація (з .NET runtime)

```bash
# Windows x64
dotnet publish BlackCat.Service/BlackCat.Service.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o ./publish/service

dotnet publish BlackCat.UI/BlackCat.UI.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o ./publish/ui
```

#### Framework-dependent публікація (потрібен .NET Runtime на цільовій машині)

```bash
dotnet publish BlackCat.Service/BlackCat.Service.csproj \
    -c Release \
    -r win-x64 \
    --self-contained false \
    -o ./publish/service

dotnet publish BlackCat.UI/BlackCat.UI.csproj \
    -c Release \
    -r win-x64 \
    --self-contained false \
    -o ./publish/ui
```

---

## Структура вихідних файлів

Після збірки:

```
BlackCat/
├── BlackCat.Shared/bin/Release/net8.0/
│   └── BlackCat.Shared.dll
├── BlackCat.Crypto/bin/Release/net8.0/
│   └── BlackCat.Crypto.dll
├── BlackCat.NetworkCore/bin/Release/net8.0/
│   └── BlackCat.NetworkCore.dll
├── BlackCat.Core/bin/Release/net8.0/
│   └── BlackCat.Core.dll
├── BlackCat.Service/bin/Release/net8.0-windows/
│   ├── BlackCat.Service.exe
│   ├── BlackCat.Service.dll
│   ├── appsettings.json
│   └── [залежності]
└── BlackCat.UI/bin/Release/net8.0-windows/
    ├── BlackCat.UI.exe
    ├── BlackCat.UI.dll
    └── [залежності]
```

---

## Розгортання

### Варіант 1: Windows Service (рекомендовано)

#### 1.1. Опублікувати сервіс

```bash
dotnet publish BlackCat.Service/BlackCat.Service.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o C:\BlackCat\Service
```

#### 1.2. Налаштувати appsettings.json

```json
{
  "BlackCat": {
    "MasterSecret": "YOUR_SECURE_PASSWORD_HERE_256_BIT",
    "DatabasePath": "C:\\BlackCat\\Data\\blackcat.db",
    "TunnelPort": 9999,
    "DefaultAllow": false,
    "EnablePacketInterception": true
  },
  "Serilog": {
    "MinimumLevel": "Information"
  }
}
```

**⚠️ ВАЖЛИВО:** Змініть `MasterSecret`!

#### 1.3. Встановити як Windows Service

```powershell
# Відкрити PowerShell як Адміністратор

# Створити службу
sc create BlackCatFirewall `
    binPath="C:\BlackCat\Service\BlackCat.Service.exe" `
    DisplayName="BlackCat Firewall" `
    start=auto

# Встановити опис
sc description BlackCatFirewall "Брандмауер з кватерніонним шифруванням"

# Запустити службу
sc start BlackCatFirewall

# Перевірити статус
sc query BlackCatFirewall
```

#### 1.4. Видалення служби (якщо потрібно)

```powershell
# Зупинити
sc stop BlackCatFirewall

# Видалити
sc delete BlackCatFirewall
```

### Варіант 2: Standalone UI

```bash
# Просто запустити UI
cd BlackCat.UI/bin/Release/net8.0-windows
.\BlackCat.UI.exe
```

**Важливо:** Запустіть з правами адміністратора!

```powershell
# З PowerShell
Start-Process "BlackCat.UI.exe" -Verb RunAs
```

---

## Налаштування прав доступу

### Дозволити Raw Socket

BlackCat використовує Raw Sockets для перехоплення пакетів, що вимагає прав адміністратора.

**Варіант 1: Запуск з правами адміністратора**

```powershell
Start-Process "BlackCat.UI.exe" -Verb RunAs
```

**Варіант 2: Налаштування постійних прав (Windows Service)**

При встановленні як Windows Service, служба автоматично працює з системними правами.

---

## Збірка з Visual Studio

### 1. Відкрити рішення

1. Запустити Visual Studio 2022
2. File → Open → Project/Solution
3. Вибрати `BlackCat/BlackCat.sln`

### 2. Налаштувати стартовий проект

- **Для тестування UI:** Right-click на `BlackCat.UI` → Set as Startup Project
- **Для тестування Service:** Right-click на `BlackCat.Service` → Set as Startup Project

### 3. Зібрати рішення

- Build → Build Solution (Ctrl+Shift+B)
- Або: Build → Rebuild Solution

### 4. Запустити з дебагером

- Debug → Start Debugging (F5)
- Або: Debug → Start Without Debugging (Ctrl+F5)

**Важливо:** Visual Studio потрібно запускати з правами адміністратора!

### 5. Публікація через Visual Studio

1. Right-click на проекті (BlackCat.Service або BlackCat.UI)
2. Publish...
3. Вибрати ціль:
   - Folder
   - TargetRuntime: win-x64
   - Deployment mode: Self-contained або Framework-dependent
4. Натиснути Publish

---

## Тестування

### Unit тести (TODO)

```bash
# Коли будуть додані тести
dotnet test BlackCat.Tests/BlackCat.Tests.csproj
```

### Інтеграційне тестування

#### 1. Запустити два екземпляри

**Машина 1 (Сервер):**
```bash
cd BlackCat.Service/bin/Release/net8.0-windows
.\BlackCat.Service.exe
```

**Машина 2 (Клієнт):**
```bash
cd BlackCat.UI/bin/Release/net8.0-windows
.\BlackCat.UI.exe
```

#### 2. Налаштувати правила тунелювання

У BlackCat.UI на клієнті додати правило:

```
Назва: Тунель до сервера
IP: [IP сервера]
Порт: 9999
Дія: Tunnel
```

#### 3. Перевірити з'єднання

Спробувати відправити дані через тунель і перевірити логи обох сторін.

---

## Troubleshooting

### Помилка: "Raw socket requires administrator privileges"

**Рішення:**
Запустіть програму з правами адміністратора.

```powershell
Start-Process "BlackCat.UI.exe" -Verb RunAs
```

### Помилка: "Could not load file or assembly"

**Рішення:**
Перезібрати рішення з чистим кешем.

```bash
dotnet clean
dotnet restore
dotnet build
```

### Помилка: "Port 9999 already in use"

**Рішення:**
Змінити порт в `appsettings.json`:

```json
{
  "BlackCat": {
    "TunnelPort": 10000
  }
}
```

### Помилка: "Database is locked"

**Рішення:**
Закрити всі інші екземпляри програми, які використовують БД.

```bash
# Перевірити процеси
tasklist | findstr BlackCat

# Вбити процес
taskkill /F /IM BlackCat.Service.exe
```

---

## CI/CD

### GitHub Actions (приклад)

```yaml
name: Build BlackCat

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: windows-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 8.0.x

    - name: Restore dependencies
      run: dotnet restore BlackCat/BlackCat.sln

    - name: Build
      run: dotnet build BlackCat/BlackCat.sln --no-restore --configuration Release

    - name: Test
      run: dotnet test BlackCat/BlackCat.sln --no-build --configuration Release

    - name: Publish Service
      run: |
        dotnet publish BlackCat/BlackCat.Service/BlackCat.Service.csproj `
          -c Release `
          -r win-x64 `
          --self-contained true `
          -p:PublishSingleFile=true `
          -o ./artifacts/service

    - name: Publish UI
      run: |
        dotnet publish BlackCat/BlackCat.UI/BlackCat.UI.csproj `
          -c Release `
          -r win-x64 `
          --self-contained true `
          -p:PublishSingleFile=true `
          -o ./artifacts/ui

    - name: Upload artifacts
      uses: actions/upload-artifact@v3
      with:
        name: BlackCat-Release
        path: ./artifacts/
```

---

## Версіонування

Використовується Semantic Versioning 2.0.0:

```
MAJOR.MINOR.PATCH

- MAJOR: Несумісні зміни API
- MINOR: Нові функції (зворотно-сумісні)
- PATCH: Виправлення помилок
```

Поточна версія: **1.0.0**

---

## Контрибуція

### Workflow для розробників

1. Fork репозиторій
2. Створити feature branch
   ```bash
   git checkout -b feature/amazing-feature
   ```
3. Внести зміни
4. Перевірити збірку
   ```bash
   dotnet build --configuration Release
   ```
5. Закомітити
   ```bash
   git commit -m "Add amazing feature"
   ```
6. Запушити
   ```bash
   git push origin feature/amazing-feature
   ```
7. Створити Pull Request

---

## Ліцензія

MIT License - Дивіться [LICENSE](LICENSE)

---

**BlackCat Firewall Build Guide** v1.0.0
