# 📚 DCS - Повна Документація Проектів

**Дата оновлення:** 01.05.2026  
**Версія:** 1.0.0

---

## 📋 Зміст

1. [Огляд репозиторію](#огляд-репозиторію)
2. [BlackCat Firewall](#blackcat-firewall)
3. [GrayCat Solution](#graycat-solution)
4. [CatSuite](#catsuite)
5. [Geocadastr](#geocadastr)
6. [Технологічний стек](#технологічний-стек)
7. [Розгортання](#розгортання)

---

## 🎯 Огляд репозиторію

Репозиторій **DCS** містить набір проектів для документообігу та захищеної мережевої комунікації:

```
DCS/
├── BlackCat/              # 🐱 Брандмауер з MQE шифруванням
├── GrayCatSolution/       # 🐈 Система документообігу
├── CatSuite/              # 📦 Пакет утиліт
├── CatSuite.Installer/    # 📥 Інсталятор
├── Geocadastr_0_1/        # 🗺️ Геокадастр
└── Лабораторні роботи/    # 📖 Навчальні матеріали
```

---

# 🐱 BlackCat Firewall

## Опис проекту

**BlackCat Firewall** - це брандмауер нового покоління з унікальною системою ідентифікації **Black-ID** та криптографією на основі кватерніонів (MQE - Modular Quaternion Encryption).

### 🎯 Основні можливості

#### 1. **Система ідентифікації Black-ID**

Black-ID - це унікальний ідентифікатор вузла мережі у форматі:

```
[РОЛЬ]-[МІСТО]-[НАЗВА]-[КОД]
```

**Приклад:** `SKLAD-ODESA-SERVER-7X99`

**Компоненти:**
- **Роль:** SKLAD, OFFICE, MAIN, BACKUP, SERVER, WORKSTATION
- **Місто:** KYIV, ODESA, LVIV, KHARKIV, DNIPRO, ZAPORIZHZHIA, KRYVYIRIH, MYKOLAIV, MARIUPOL, VINNYTSIA, KHERSON, POLTAVA
- **Назва:** Користувацька назва (латиниця, цифри)
- **Код:** 4-символьний унікальний код (автогенерація)

**Особливості:**
- ✅ Hardware Fingerprinting (CPU ID + Motherboard Serial + MAC Address)
- ✅ Криптографічний підпис для верифікації
- ✅ Зберігання в базі даних SQLite
- ✅ Автоматичне завантаження при старті

#### 2. **Захищені тунелі (Secure Tunnels)**

**Функціонал:**
- 📞 **Телефонна книга** - зберігання віддалених вузлів
- 🤝 **Ручне парування** - додавання вузлів за Black-ID
- 🔐 **Handshake протокол** - автентифікація з Hardware Fingerprint
- 🔄 **NAT Traversal** - підтримка динамічних IP
- 👻 **Stealth Mode** - приховування від сканування при невдалому handshake

**Handshake протокол:**

```
Клієнт                    Сервер
   |                         |
   |----> HELLO (Black-ID) ->|
   |                         | (перевірка Black-ID)
   |<--- CHALLENGE (nonce) <-|
   |                         |
   | (обчислення response)   |
   |                         |
   |----> RESPONSE --------->|
   |                         | (перевірка HW fingerprint)
   |<--- HANDSHAKE (session)-|
   |                         |
   |===== З'єднання встановлено =====
```

#### 3. **MQE Шифрування**

**Modular Quaternion Encryption** - власний алгоритм на основі кватерніонів Гамільтона.

**Кватерніон:**
```
Q = w + xi + yj + zk
де i² = j² = k² = ijk = -1
```

**Процес шифрування:**
1. Генерація ключа з MasterSecret + Timestamp
2. SHA256 хешування → 4 компоненти кватерніона
3. Множення даних на ключ (множення Гамільтона)
4. Модуль 256 для байтового діапазону

**Переваги:**
- ⚡ Швидкість - тільки цілочисельна арифметика
- 🔒 Стійкість до частотного аналізу
- 🕐 Динамічний ключ (кожен пакет)
- ✅ Захист від Replay Attack (5 сек timestamp)

#### 4. **Фільтрація трафіку**

**Правила фільтрації:**
- IP адреси та підмережі (CIDR: 192.168.1.0/24)
- Порти (одиночні або діапазони)
- Протоколи (TCP, UDP, ICMP, Any)
- Напрямок (Inbound, Outbound, Both)
- Дії (Allow, Block, Tunnel)
- Пріоритети

**Контроль програм:**
- Прив'язка правил до процесів
- Вибір з активних портів
- Вибір з запущених процесів

#### 5. **Моніторинг та статистика**

**Real-time графіки:**
- Швидкість (KB/s) - стовпчаста діаграма
- Трафік по програмах - Top-5 процесів (лінійні графіки)
- Часова вісь (mm:ss формат)
- Легенда з кольорами

**Метрики:**
- Total Packets - загальна кількість пакетів
- Allowed - дозволені пакети
- Blocked - заблоковані пакети
- Tunneled - відправлені через тунель
- Speed - швидкість у KB/s

**Таблиці:**
- Статистика процесів (назва, пакети, трафік)
- Правила фільтрації (IP, порт, протокол, дія)
- Тунелі (Black-ID, статус, статистика)

#### 6. **Користувацький інтерфейс**

**Дизайн:**
- 🎨 Темна тема (#1E1E1E фон)
- 🟢 Зелені акценти (#4EC9B0)
- 📊 Таблиці з прозорим фоном та зеленими рамками
- 📈 LiveCharts для real-time графіків
- 🪟 MahApps.Metro для сучасного вигляду

**Вкладки:**
1. **Панель** - статистика та графіки
2. **Правила** - управління фільтрацією
3. **🔐 Тунелі** - Black-ID та з'єднання
4. **📋 Лог** - детальні події

**Діалоги:**
- Створення Black-ID (роль, місто, назва)
- Додавання тунелю (Black-ID, IP, порт)
- Вибір активних портів
- Вибір запущених процесів
- Керування правилами (CRUD)

---

## 📁 Структура проекту BlackCat

```
BlackCat/
├── BlackCat.sln                      # Visual Studio Solution
│
├── BlackCat.Shared/                  # 📦 Загальні моделі
│   ├── Models/
│   │   ├── BlackID.cs               # Black-ID модель
│   │   ├── PeerNode.cs              # Віддалений вузол
│   │   ├── PacketInfo.cs            # Інформація про пакет
│   │   ├── FilterRule.cs            # Правило фільтрації
│   │   ├── TunnelPacket.cs          # Зашифрований пакет
│   │   ├── FirewallStatistics.cs    # Статистика
│   │   ├── ConnectionEvent.cs       # Подія з'єднання
│   │   └── HardwareInfo.cs          # Інформація про залізо
│   └── Enums/
│       ├── ProtocolType.cs          # TCP, UDP, ICMP, Any
│       ├── FilterAction.cs          # Allow, Block, Tunnel
│       ├── TrafficDirection.cs      # Inbound, Outbound, Both
│       ├── TunnelStatus.cs          # Connected, Disconnected, ...
│       ├── EventType.cs             # HelloReceived, ChallengeReceived, ...
│       └── ConnectionDirection.cs   # Incoming, Outgoing
│
├── BlackCat.Crypto/                  # 🔐 Криптографія
│   ├── Quaternion.cs                # Математичний кватерніон
│   │   ├── Multiply (Гамільтона)
│   │   ├── ModularInverse
│   │   ├── Normalize (mod 256)
│   │   └── ToBytes / FromBytes
│   └── MQECryptoService.cs          # MQE алгоритм
│       ├── Encrypt (byte[] → byte[])
│       ├── Decrypt (byte[] → byte[])
│       └── GenerateKey (secret + timestamp)
│
├── BlackCat.NetworkCore/             # 🌐 Мережевий рівень
│   ├── SecureTunnelService.cs       # Захищений тунель
│   │   ├── ConfigureBlackID
│   │   ├── ConnectToNode
│   │   ├── SendThroughTunnel
│   │   ├── StealthMode
│   │   └── Events (PacketReceived, StatusChanged, AuthenticationFailed)
│   ├── PacketInterceptor.cs         # Перехоплення пакетів
│   │   ├── StartCapture (Raw Sockets)
│   │   ├── StopCapture
│   │   └── Events (PacketCaptured, InterceptorError)
│   └── Handshake/
│       ├── HelloMessage.cs          # Крок 1: Привітання
│       ├── ChallengeMessage.cs      # Крок 2: Виклик
│       ├── ResponseMessage.cs       # Крок 3: Відповідь
│       └── HandshakeMessage.cs      # Крок 4: Підтвердження
│
├── BlackCat.Core/                    # 💼 Бізнес-логіка
│   ├── FirewallCoordinator.cs       # Головний координатор
│   │   ├── StartAsync / StopAsync
│   │   ├── ConfigureBlackID
│   │   ├── LoadRules
│   │   ├── Statistics
│   │   └── Events (LogMessage, StatisticsUpdated)
│   ├── FilterEngine.cs              # Двигун фільтрації
│   │   ├── CheckPacket (правила)
│   │   ├── FilterAction (результат)
│   │   └── Events (PacketFiltered)
│   ├── Services/
│   │   ├── BlackIDService.cs        # Генерація Black-ID
│   │   │   ├── GenerateID (role, city, name)
│   │   │   └── ValidateID (формат)
│   │   ├── HandshakeService.cs      # Handshake протокол
│   │   │   ├── HandleHello
│   │   │   ├── HandleChallenge
│   │   │   ├── HandleResponse
│   │   │   └── HandleHandshake
│   │   ├── HardwareFingerprintService.cs  # HW fingerprint
│   │   │   ├── GetHardwareInfo (CPU, MB, MAC)
│   │   │   ├── GenerateFingerprint (SHA256)
│   │   │   └── ValidateFingerprint
│   │   └── ProcessLookupService.cs  # Пошук процесів
│   │       ├── GetActiveTcpConnections
│   │       ├── GetActiveUdpConnections
│   │       └── GetProcessByPort
│   └── Data/
│       ├── BlackCatDatabase.cs      # Ініціалізація БД
│       ├── RuleRepository.cs        # Правила фільтрації
│       ├── BlackIDRepository.cs     # Локальний Black-ID
│       ├── PeerNodeRepository.cs    # Телефонна книга
│       └── ConnectionEventRepository.cs  # Логи з'єднань
│
├── BlackCat.Service/                 # 🔧 Windows Service
│   ├── Program.cs                   # Host Builder
│   ├── BlackCatWorker.cs            # Background Worker
│   └── appsettings.json             # Конфігурація
│       ├── MasterSecret
│       ├── DatabasePath
│       ├── TunnelPort (9999)
│       └── DefaultAllow (false)
│
└── BlackCat.UI/                      # 🖥️ WPF інтерфейс
    ├── MainWindow.xaml              # Головне вікно
    │   ├── Статистика (графіки)
    │   ├── Правила фільтрації
    │   ├── Тунелі (Black-ID)
    │   └── Лог подій
    ├── MainWindow.xaml.cs           # Code-behind
    ├── SettingsWindow.xaml          # Налаштування
    │   ├── Black-ID створення
    │   ├── Hardware Info
    │   ├── Tunnel Port
    │   └── Stealth Mode
    ├── BlackIDCreationDialog.xaml   # Створення Black-ID
    ├── AddTunnelDialog.xaml         # Додавання тунелю
    ├── RulesManagementWindow.xaml   # Керування правилами
    ├── ActivePortsDialog.xaml       # Вибір портів
    └── RunningProcessesDialog.xaml  # Вибір процесів
```

---

## 🗄️ База даних

**SQLite** (`blackcat.db`)

### Таблиці:

#### 1. **LocalBlackID**
```sql
CREATE TABLE LocalBlackID (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FullID TEXT NOT NULL UNIQUE,         -- SKLAD-ODESA-SERVER-7X99
    Role TEXT NOT NULL,                   -- SKLAD
    City TEXT NOT NULL,                   -- ODESA
    Name TEXT NOT NULL,                   -- SERVER
    Code TEXT NOT NULL,                   -- 7X99
    HardwareFingerprint TEXT NOT NULL,    -- SHA256(CPU+MB+MAC)
    Signature TEXT NOT NULL,              -- Криптопідпис
    CreatedAt TEXT NOT NULL,              -- ISO8601
    IsActive INTEGER DEFAULT 1            -- 1 = активний
);
```

#### 2. **PeerNodes** (Телефонна книга)
```sql
CREATE TABLE PeerNodes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    BlackID TEXT NOT NULL UNIQUE,        -- Віддалений Black-ID
    Address TEXT NOT NULL,                -- IP або домен
    Port INTEGER NOT NULL DEFAULT 9999,
    DisplayName TEXT NOT NULL,            -- Назва для відображення
    Description TEXT,                     -- Опис вузла
    IsTrusted INTEGER DEFAULT 0,          -- Довірений вузол
    LastConnectedAt TEXT,                 -- Остання успішна з'єднання
    CreatedAt TEXT NOT NULL,
    IsActive INTEGER DEFAULT 1,
    SuccessfulConnections INTEGER DEFAULT 0,
    FailedConnections INTEGER DEFAULT 0,
    PublicKey TEXT,                       -- Публічний ключ (майбутнє)
    Tags TEXT                             -- JSON масив тегів
);
```

#### 3. **ConnectionEvents** (Логи з'єднань)
```sql
CREATE TABLE ConnectionEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RemoteBlackID TEXT,                   -- Black-ID віддаленого вузла
    RemoteIP TEXT NOT NULL,
    RemotePort INTEGER NOT NULL,
    EventType INTEGER NOT NULL,           -- HelloReceived, ChallengeReceived, ...
    Direction INTEGER NOT NULL,           -- Incoming, Outgoing
    Message TEXT NOT NULL,                -- Опис події
    ErrorDetails TEXT,                    -- Деталі помилки
    IsAuthenticated INTEGER DEFAULT 0,    -- Успішна автентифікація
    Timestamp TEXT NOT NULL,
    DurationSeconds REAL,                 -- Тривалість з'єднання
    BytesSent INTEGER DEFAULT 0,
    BytesReceived INTEGER DEFAULT 0
);
```

#### 4. **FilterRules** (Правила фільтрації)
```sql
CREATE TABLE FilterRules (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    IPAddress TEXT,                       -- 192.168.1.0/24
    Port INTEGER,
    PortRange TEXT,                       -- 8000-9000
    Protocol INTEGER,                     -- TCP=1, UDP=2, ICMP=3
    Action INTEGER,                       -- Allow=1, Block=2, Tunnel=3
    Direction INTEGER,                    -- Inbound=1, Outbound=2, Both=3
    ProcessName TEXT,                     -- chrome.exe
    IsEnabled INTEGER DEFAULT 1,
    Priority INTEGER DEFAULT 100,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    Description TEXT
);
```

---

## 🔧 API та інтеграція

### FirewallCoordinator API

```csharp
// Запуск брандмауера
var coordinator = new FirewallCoordinator(
    masterSecret: "YourSecret",
    databasePath: "blackcat.db",
    tunnelPort: 9999
);

await coordinator.StartAsync();

// Створення Black-ID
var blackID = coordinator.ConfigureBlackID(
    role: "MAIN",
    city: "KYIV",
    name: "SERVER"
);
// Результат: MAIN-KYIV-SERVER-7X99

// Завантаження правил
coordinator.LoadRules();

// Підписка на події
coordinator.LogMessage += (sender, message) => {
    Console.WriteLine(message);
};

coordinator.StatisticsUpdated += (sender, stats) => {
    Console.WriteLine($"Speed: {stats.BytesPerSecond / 1024:F2} KB/s");
};

// Статистика
var stats = coordinator.Statistics;
Console.WriteLine($"Total: {stats.TotalPackets}");
Console.WriteLine($"Allowed: {stats.AllowedPackets}");
Console.WriteLine($"Blocked: {stats.BlockedPackets}");
Console.WriteLine($"Tunneled: {stats.TunneledPackets}");

// Зупинка
await coordinator.StopAsync();
```

### Додавання правил програмно

```csharp
var repository = new RuleRepository("blackcat.db");

// Дозволити локальну мережу
repository.AddRule(new FilterRule
{
    Name = "Дозволити локальну мережу 192.168.x.x",
    IPAddress = "192.168.0.0/16",
    Protocol = ProtocolType.Any,
    Action = FilterAction.Allow,
    Direction = TrafficDirection.Both,
    Priority = 10,
    IsEnabled = true
});

// Тунелювати SSH
repository.AddRule(new FilterRule
{
    Name = "Захищений тунель SSH",
    Port = 22,
    Protocol = ProtocolType.TCP,
    Action = FilterAction.Tunnel,
    Direction = TrafficDirection.Outbound,
    Priority = 50,
    IsEnabled = true
});

// Заблокувати підозрілий IP
repository.AddRule(new FilterRule
{
    Name = "Блокувати шкідливий сервер",
    IPAddress = "203.0.113.0",
    Protocol = ProtocolType.Any,
    Action = FilterAction.Block,
    Direction = TrafficDirection.Both,
    Priority = 1,  // Найвищий пріоритет
    IsEnabled = true
});

// Перезавантажити правила
coordinator.LoadRules();
```

### Робота з Black-ID

```csharp
var blackIDService = new BlackIDService();
var hwService = new HardwareFingerprintService();

// Отримати інформацію про залізо
var hwInfo = hwService.GetHardwareInfo();
Console.WriteLine($"CPU ID: {hwInfo.CpuId}");
Console.WriteLine($"Motherboard: {hwInfo.MotherboardSerial}");
Console.WriteLine($"MAC: {hwInfo.MacAddress}");
Console.WriteLine($"Fingerprint: {hwInfo.Fingerprint}");

// Створити Black-ID
var blackID = blackIDService.GenerateID(
    role: "OFFICE",
    city: "LVIV",
    name: "WORKSTATION"
);

Console.WriteLine($"Black-ID: {blackID.FullID}");
Console.WriteLine($"HW Fingerprint: {blackID.HardwareFingerprint}");
Console.WriteLine($"Signature: {blackID.Signature}");

// Зберегти в БД
var database = new BlackCatDatabase("blackcat.db");
var repository = new BlackIDRepository(database);
repository.SaveBlackID(blackID);

// Завантажити активний Black-ID
var activeID = repository.GetActiveBlackID();
```

### Робота з тунелями

```csharp
var database = new BlackCatDatabase("blackcat.db");
var peerRepo = new PeerNodeRepository(database);

// Додати вузол до телефонної книги
var peer = new PeerNode
{
    BlackID = "SKLAD-ODESA-SERVER-7X99",
    Address = "192.168.1.100",
    Port = 9999,
    DisplayName = "Склад в Одесі",
    Description = "Головний сервер складу",
    IsTrusted = true,
    Tags = "[\"виробництво\", \"склад\"]"
};

peerRepo.AddPeerNode(peer);

// Отримати всі вузли
var peers = peerRepo.GetAllPeerNodes();
foreach (var p in peers)
{
    Console.WriteLine($"{p.DisplayName}: {p.BlackID} @ {p.Address}:{p.Port}");
}

// З'єднатися з вузлом
var tunnelService = new SecureTunnelService("MasterSecret", 9999);
tunnelService.ConfigureBlackID(
    ourBlackID: activeID,
    handleHello: (hello, ip) => handshakeService.HandleHello(hello, activeID, ip),
    handleResponse: (response, ip) => handshakeService.HandleResponse(response, ip)
);

await tunnelService.ConnectToNode(peer.Address, peer.Port);
```

---

## 🚀 Встановлення та запуск

### Вимоги

- **Windows 10/11** (x64)
- **.NET 8.0 Runtime** ([завантажити](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Права адміністратора** (для Raw Sockets)

### Збірка проекту

```bash
# Клонувати репозиторій
git clone https://github.com/kotr228/DCS.git
cd DCS/BlackCat

# Відновити NuGet пакети
dotnet restore BlackCat.sln

# Зібрати в Release
dotnet build BlackCat.sln --configuration Release

# Запустити UI
cd BlackCat.UI
dotnet run

# Або подвійним кліком на BlackCat.UI.exe
```

### Запуск з правами адміністратора

**PowerShell:**
```powershell
Start-Process "BlackCat.UI.exe" -Verb RunAs
```

**CMD:**
```cmd
runas /user:Administrator "C:\Path\To\BlackCat.UI.exe"
```

### Встановлення як Windows Service

```bash
cd BlackCat.Service

# Публікація
dotnet publish -c Release -r win-x64 --self-contained

# Створення сервісу
sc create BlackCatFirewall binPath="C:\BlackCat\BlackCat.Service.exe"
sc description BlackCatFirewall "BlackCat Firewall with MQE encryption"

# Запуск
sc start BlackCatFirewall

# Перевірка статусу
sc query BlackCatFirewall

# Зупинка
sc stop BlackCatFirewall

# Видалення
sc delete BlackCatFirewall
```

### Налаштування конфігурації

**appsettings.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "BlackCat": {
    "MasterSecret": "YourVerySecure256BitSecretKeyHere!Change!",
    "DatabasePath": "blackcat.db",
    "TunnelPort": 9999,
    "DefaultAllow": false,
    "EnablePacketInterception": true,
    "StealthMode": true,
    "TimestampValiditySeconds": 5,
    "MaxConnectionsPerNode": 10
  }
}
```

**⚠️ ВАЖЛИВО:**
- Змініть `MasterSecret` на унікальний пароль (мінімум 32 символи)
- `DefaultAllow: false` = за замовчуванням блокувати все
- `StealthMode: true` = приховування від сканування

---

## 📊 Моніторинг та логування

### Логи

Логи зберігаються в папці `logs/`:

```
logs/
├── blackcat-20260501.log
├── blackcat-20260502.log
└── blackcat-20260503.log
```

**Формат логів:**
```
[2026-05-01 15:30:45.123] [INF] BlackCat Firewall Service запущено
[2026-05-01 15:30:46.456] [INF] 📝 Black-ID: MAIN-KYIV-SERVER-7X99
[2026-05-01 15:30:47.789] [INF] ✅ Правила фільтрації завантажено: 12 шт.
[2026-05-01 15:31:00.001] [INF] 🚫 Заблоковано: 192.168.1.100:54321 → 203.0.113.1:80
[2026-05-01 15:31:05.234] [INF] ✅ Дозволено: 192.168.1.50:443 → 8.8.8.8:53 (DNS)
[2026-05-01 15:31:10.567] [INF] 🔒 Тунель: 10.0.0.5:22 → SKLAD-ODESA-SERVER-7X99
[2026-05-01 15:31:15.890] [WRN] ⚠️ Handshake failed: Invalid HW fingerprint
[2026-05-01 15:31:20.123] [ERR] ❌ Помилка перехоплення: Access Denied (потрібні права адміна)
```

### Метрики

**Доступ через API:**
```csharp
var stats = coordinator.Statistics;

// Базова статистика
Console.WriteLine($"Пакетів загалом: {stats.TotalPackets:N0}");
Console.WriteLine($"Дозволено: {stats.AllowedPackets:N0}");
Console.WriteLine($"Заблоковано: {stats.BlockedPackets:N0}");
Console.WriteLine($"Тунельовано: {stats.TunneledPackets:N0}");
Console.WriteLine($"Помилок: {stats.ErrorPackets:N0}");

// Продуктивність
Console.WriteLine($"Швидкість: {stats.BytesPerSecond / 1024:F2} KB/s");
Console.WriteLine($"Затримка: {stats.AverageLatencyMs:F2} ms");

// Час роботи
var uptime = DateTime.UtcNow - stats.ServiceStartTime;
Console.WriteLine($"Час роботи: {uptime:dd\\.hh\\:mm\\:ss}");

// Статус тунелю
Console.WriteLine($"Статус тунелю: {stats.TunnelStatus}");
```

---

## 🔒 Безпека

### Захист від загроз

| Загроза | Захист |
|---------|--------|
| Replay Attack | Timestamp validation (5 сек) |
| Man-in-the-Middle | MQE шифрування + Checksum |
| Сканування портів | Stealth Mode (не відповідає на невалідні handshake) |
| Підміна вузла | Hardware Fingerprint + Signature |
| Brute Force | Rate limiting (TODO) |
| DDoS | Connection limits per node |

### Best Practices

1. **Регулярно змінюйте MasterSecret** (раз на місяць)
2. **Використовуйте складні паролі** (мінімум 32 символи, різні типи)
3. **Обмежуйте доступ до БД** (blackcat.db містить чутливі дані)
4. **Моніторте логи** на підозрілу активність
5. **Оновлюйте правила** фільтрації регулярно
6. **Бекап БД** перед великими змінами
7. **Тестуйте правила** в sandbox середовищі

---

## 🛠️ Розробка

### Додавання нової функціональності

#### Приклад: Новий тип шифрування

```csharp
// 1. Створити інтерфейс
public interface ICryptoService
{
    byte[] Encrypt(byte[] data, string key);
    byte[] Decrypt(byte[] data, string key);
}

// 2. Реалізувати в BlackCat.Crypto
public class AESCryptoService : ICryptoService
{
    public byte[] Encrypt(byte[] data, string key)
    {
        // AES-256 implementation
    }

    public byte[] Decrypt(byte[] data, string key)
    {
        // AES-256 decryption
    }
}

// 3. Додати вибір в SecureTunnelService
public SecureTunnelService(string masterSecret, int port, CryptoType type = CryptoType.MQE)
{
    _cryptoService = type switch
    {
        CryptoType.MQE => new MQECryptoService(),
        CryptoType.AES => new AESCryptoService(),
        _ => throw new ArgumentException("Unknown crypto type")
    };
}
```

### Тестування

```bash
# Unit тести (TODO)
dotnet test BlackCat.Tests

# Integration тести
dotnet test BlackCat.IntegrationTests

# Performance benchmark
dotnet run --project BlackCat.Benchmark -c Release
```

### Code Style

```csharp
// ✅ Добре
public class FilterEngine
{
    private readonly RuleRepository _repository;

    public FilterEngine(RuleRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public FilterAction CheckPacket(PacketInfo packet)
    {
        var rules = _repository.GetEnabledRules()
            .OrderBy(r => r.Priority)
            .ToList();

        foreach (var rule in rules)
        {
            if (RuleMatches(packet, rule))
                return rule.Action;
        }

        return FilterAction.Block;  // Default deny
    }
}

// ❌ Погано
public class filterengine {
    public void checkpacket(object p) {
        // mixed naming, no error handling
    }
}
```

---

## 📈 Roadmap

### v1.1.0 (Q2 2026)
- [ ] REST API для віддаленого керування
- [ ] Web Dashboard (ASP.NET Core)
- [ ] Підтримка IPv6
- [ ] Rate limiting для захисту від DDoS

### v1.2.0 (Q3 2026)
- [ ] WinPcap/Npcap інтеграція для драйверного рівня
- [ ] WFP (Windows Filtering Platform) драйвер
- [ ] Deep Packet Inspection (DPI)
- [ ] Application layer filtering

### v2.0.0 (Q4 2026)
- [ ] Linux підтримка (iptables/nftables)
- [ ] Docker containerization
- [ ] Machine Learning для аномалій
- [ ] Cloud dashboard з централізованим управлінням

---

## 🐛 Відомі проблеми

1. **Raw Sockets обмеження**
   - Потребує Npcap для повної функціональності
   - Деякі версії Windows блокують Raw Sockets
   - **Рішення:** Встановити [Npcap](https://npcap.com/)

2. **Права адміністратора**
   - Перехоплення пакетів вимагає підвищених прав
   - **Рішення:** Запускати з правами адміністратора

3. **Firewall конфлікти**
   - Може конфліктувати з Windows Defender Firewall
   - **Рішення:** Додати виключення або вимкнути WDF

---

# 🐈 GrayCat Solution

## Опис

**GrayCat** - система документообігу та управління файлами.

### Структура

```
GrayCatSolution/
├── GrayCat.Core/      # Бізнес-логіка
├── GrayCat.Shared/    # Загальні моделі
├── GrayCat.Service/   # Windows Service
└── GrayCat.UI/        # WPF інтерфейс
```

---

# 📦 CatSuite

## Опис

**CatSuite** - пакет утиліт для роботи з Cat-сім'єю додатків.

### Компоненти

- **CatSuite** - основний пакет
- **CatSuite.Installer** - інсталятор
- **CatSuite.Launcher** - лаунчер

---

# 🗺️ Geocadastr

## Опис

**Geocadastr** - система геокадастру для управління земельними ділянками.

---

## 🛠️ Технологічний стек (загальний)

| Категорія | Технологія | Версія |
|-----------|------------|--------|
| **Платформа** | .NET | 8.0 |
| **Мова** | C# | 12.0 |
| **UI Framework** | WPF | .NET 8.0 |
| **База даних** | SQLite | 3.x |
| **Логування** | Serilog | 3.x |
| **UI Library** | MahApps.Metro | 2.4.11 |
| **Графіки** | LiveCharts.Wpf | 0.9.7 |
| **Testing** | xUnit | 2.x |
| **Benchmark** | BenchmarkDotNet | 0.13.x |

---

## 📥 Розгортання

### Розгортання на Production

```bash
# 1. Збірка всіх проектів
cd DCS
dotnet clean
dotnet restore
dotnet build --configuration Release

# 2. Публікація BlackCat
cd BlackCat/BlackCat.Service
dotnet publish -c Release -r win-x64 --self-contained -o publish

# 3. Створення інсталяційного пакету
cd ../CatSuite.Installer
dotnet build --configuration Release

# 4. Копіювання на сервер
robocopy publish \\server\BlackCat /MIR

# 5. Встановлення на сервері
sc create BlackCatFirewall binPath="C:\BlackCat\BlackCat.Service.exe"
sc start BlackCatFirewall
```

### Docker (майбутнє)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["BlackCat/", "BlackCat/"]
RUN dotnet restore "BlackCat/BlackCat.sln"
RUN dotnet build "BlackCat/BlackCat.sln" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BlackCat/BlackCat.Service/BlackCat.Service.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BlackCat.Service.dll"]
```

---

## 📞 Підтримка

**Email:** support@blackcat-firewall.com  
**GitHub Issues:** https://github.com/kotr228/DCS/issues  
**Documentation:** https://docs.blackcat-firewall.com

---

## 📄 Ліцензія

**MIT License**

Copyright (c) 2026 DCS Team

---

## ✍️ Автори

**DCS Development Team**
- Архітектура: AI Assistant
- Розробка: kotr228
- Тестування: QA Team

---

**Документація оновлена:** 01.05.2026  
**Версія:** 1.0.0  
**Репозиторій:** https://github.com/kotr228/DCS
