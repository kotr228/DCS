# BlackCat — Збірка та розгортання

## Вимоги

| Інструмент | Версія |
|------------|--------|
| .NET SDK | 8.0+ |
| Windows | 10 / 11 (x64) |
| Visual Studio | 2022+ або VS Code з C# Extension |
| Права | Адміністратор (для Raw Socket та UPnP) |

---

## Структура рішення

```
BlackCat.sln
├── BlackCat.Shared        net8.0           (без UI-залежностей)
├── BlackCat.Crypto        net8.0
├── BlackCat.NetworkCore   net8.0
├── BlackCat.Core          net8.0
├── BlackCat.Service       net8.0-windows   (Windows Service)
└── BlackCat.UI            net8.0-windows   (WPF)
```

---

## Збірка

### Через командний рядок

```bat
:: Перейти в папку з рішенням
cd BlackCat

:: Відновити пакети
dotnet restore

:: Зібрати все рішення
dotnet build BlackCat.sln --configuration Release

:: Зібрати тільки UI (для розробки)
dotnet build BlackCat.UI/BlackCat.UI.csproj --configuration Debug
```

### Через Visual Studio

1. Відкрити `BlackCat.sln`
2. Встановити конфігурацію: `Release` або `Debug`
3. `Build → Build Solution` (Ctrl+Shift+B)

---

## Запуск

### UI (розробка/тестування)

```bat
:: Потрібні права адміністратора для Raw Socket
dotnet run --project BlackCat.UI/BlackCat.UI.csproj
```

Або запустити `BlackCat.UI.exe` від імені адміністратора.

### Windows Service (продакшн)

```bat
:: Зібрати як self-contained
dotnet publish BlackCat.Service/BlackCat.Service.csproj ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  --output ./publish/service

:: Встановити службу
sc create "BlackCatFirewall" ^
  binPath="C:\path\to\publish\service\BlackCat.Service.exe" ^
  start=auto ^
  DisplayName="BlackCat Firewall Service"

:: Запустити
sc start BlackCatFirewall

:: Зупинити
sc stop BlackCatFirewall

:: Видалити
sc delete BlackCatFirewall
```

---

## Перший запуск

### 1. Запустити від адміністратора

`BlackCat.UI.exe` потребує прав адміністратора для:
- `Socket(SocketType.Raw)` — перехоплення пакетів
- `netsh advfirewall` — відкриття портів у Windows Firewall
- UPnP-запити до роутера

### 2. Створити Black-ID

**Налаштування → Black-ID → Створити**

Формат: `РОЛЬ-МІСТО-НАЗВА-КОД`

Приклади:
```
MAIN-KYIV-SERVER-2N4D
SKLAD-ODESA-PC-7X99
CLIENT-KHARKIV-LAPTOP-A1B2
```

Black-ID зберігається в таблиці `LocalBlackID` бази даних `blackcat.db`.

### 3. Натиснути "Запустити"

Програма:
- Запускає `FirewallCoordinator` (Raw Socket перехоплення)
- Ініціалізує `TunnelManager` (UDP-сервер на порту 9999)
- Намагається відкрити порт через UPnP
- Визначає публічну IP через ipify.org

### 4. Підключитися до піра

Перейти на вкладку **🕳️ P2P** і виконати обмін кодами (детально в README.md).

---

## Конфігурація

### appsettings.json (BlackCat.Service)

```json
{
  "BlackCat": {
    "MasterSecret": "YourSecretPasswordHere",
    "DatabasePath": "blackcat.db",
    "TunnelPort": 9999,
    "EnablePacketInterception": true
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/blackcat-.log",
          "rollingInterval": "Day"
        }
      }
    ]
  }
}
```

### MasterSecret

Спільний секрет для MQE-шифрування. Обидва вузли **повинні мати однаковий** `MasterSecret`, інакше розшифрування не вдасться.

Поточне значення в UI: `"YourSecretPasswordHere"` (захардкоджено в `StartButton_Click`).

> Для виробничого використання: перенести `MasterSecret` в `appsettings.json` або змінний середовища.

---

## База даних

SQLite файл `blackcat.db` створюється автоматично поряд з `.exe` при першому запуску.

**Схема версіонується** через таблицю `DatabaseVersion`. При невідповідності версій — `DatabaseMigrator` застосовує міграції автоматично. Якщо міграція не вдалась — БД перестворюється з нуля.

**Розташування:** `%AppDir%\blackcat.db`

---

## Папки тимчасових файлів

Створюються автоматично при запуску UI:

```
%AppDir%\temp_data\     — системні файли (node info JSON, метадані трансферів)
%AppDir%\temp_files\    — файли отримані від пірів
```

---

## Порти та мережа

| Порт | Протокол | Призначення |
|------|----------|-------------|
| 9999 | TCP | Вхідні захищені з'єднання (handshake) |
| 9999 | UDP | P2P тунель (або динамічний після hole punch) |
| 19302 | UDP | STUN запити (stun.l.google.com) |

Для прийому вхідних P2P підключень з інтернету потрібно:
1. **UPnP** на роутері (автоматично) — або
2. **Port Forwarding**: `{публічна IP}:9999` → `{локальна IP}:9999`

Для **вихідних** підключень (ви ініціатор hole punch) — роутер не потрібен.

---

## Усунення неполадок

### "Адреса вже використовується"

Raw Socket на Windows може конфліктувати з іншими процесами або запускатись без прав адміністратора.

```bat
:: Перевірити хто займає порт 9999
netstat -ano | findstr :9999

:: Перевірити правила брандмауера
netsh advfirewall firewall show rule name="BlackCat Secure Tunnel"
```

### STUN не відповідає

- Перевірте інтернет-з'єднання
- Порт UDP 19302 має бути відкритий (зазвичай відкритий за замовчуванням)
- Спробуйте `nslookup stun.l.google.com`

### Hole punch не вдається

1. Обидва учасники повинні натиснути "З'єднати" **в один час** (допуск ±30 секунд)
2. Деякі провайдери використовують **симетричний NAT** — hole punch через нього неможливий без relay-сервера
3. Перевірте що UDP не блокується корпоративним файерволом

### BlackID не оновлюється після підключення

Можлива UNIQUE constraint помилка в БД якщо той самий BlackID вже є з попередньої сесії. Перевірте лог програми на наявність `⚠️ DB update error:`. Видаліть старі записи через **Тунелі → Видалити**.

### Файли не надходять до temp_files/

- Переконайтеся що `temp_files/` існує поряд з `BlackCat.UI.exe`
- Перевірте права запису в цю папку
- Перевірте лог на `📥 Отримано файл` або `⚠️ File receive error`
