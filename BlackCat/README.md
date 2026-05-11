# 🐱 BlackCat Firewall

**Брандмауер нового покоління з кватерніонним шифруванням (MQE)**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg)](https://www.microsoft.com/windows)

BlackCat - це передовий брандмауер, який використовує криптографію на основі кватерніонів (Modular Quaternion Encryption) для захищеної передачі даних через мережу. Розроблено для вирішення критичних проблем безпеки мережевої комунікації.

---

## 📋 Зміст

- [Особливості](#-особливості)
- [Архітектура](#-архітектура)
- [Технологічний стек](#-технологічний-стек)
- [Встановлення](#-встановлення)
- [Використання](#-використання)
- [Криптографія MQE](#-криптографія-mqe)
- [Конфігурація](#-конфігурація)
- [Розробка](#-розробка)

---

## ✨ Особливості

### 🔒 Безпека

- **Кватерніонне шифрування (MQE)** - Унікальний алгоритм шифрування на основі множення Гамільтона
- **Динамічна генерація ключів** - Ключ змінюється для кожного пакету на основі часової мітки
- **Захист від Replay Attack** - Валідація часової мітки (5 секунд)
- **Контрольна сума SHA256** - Перевірка цілісності даних
- **TLS-подібний тунель** - Захищена передача між вузлами

### 🛡️ Фільтрація

- **Whitelist/Blacklist** - Гнучка система правил фільтрації
- **CIDR підтримка** - Фільтрація за підмережами (192.168.1.0/24)
- **Протокол/Порт фільтри** - TCP, UDP, ICMP з можливістю вказання портів
- **Пріоритетні правила** - Гнучка система пріоритетів
- **Напрямок трафіку** - Inbound/Outbound/Both

### 📊 Моніторинг

- **Real-time статистика** - Графіки пакетів в реальному часі
- **WPF інтерфейс** - Сучасний темний інтерфейс з Material Design
- **Детальне логування** - Serilog з ротацією файлів
- **Продуктивність** - Моніторинг швидкості та затримки

---

## 🏗️ Архітектура

```
BlackCat/
├── BlackCat.Shared/                 # Спільні моделі та контракти
│   ├── Models/
│   │   ├── PacketInfo.cs           # Інформація про пакет
│   │   ├── FilterRule.cs           # Правило фільтрації
│   │   ├── TunnelPacket.cs         # Структура зашифрованого пакету
│   │   └── FirewallStatistics.cs   # Статистика брандмауера
│   └── Enums/
│       ├── ProtocolType.cs
│       ├── FilterAction.cs
│       ├── TrafficDirection.cs
│       └── TunnelStatus.cs
│
├── BlackCat.Crypto/                 # Криптографічний модуль
│   ├── Quaternion.cs               # Математичний кватерніон
│   └── MQECryptoService.cs         # MQE алгоритм
│
├── BlackCat.NetworkCore/            # Мережевий слой
│   ├── SecureTunnelService.cs      # Захищений тунель
│   └── PacketInterceptor.cs        # Перехоплення пакетів
│
├── BlackCat.Core/                   # Бізнес-логіка
│   ├── FilterEngine.cs             # Двигун фільтрації
│   ├── FirewallCoordinator.cs      # Головний координатор
│   └── Data/
│       └── RuleRepository.cs       # Репозиторій правил (SQLite)
│
├── BlackCat.Service/                # Windows Service
│   ├── BlackCatWorker.cs           # Worker service
│   ├── Program.cs
│   └── appsettings.json
│
└── BlackCat.UI/                     # WPF інтерфейс
    ├── MainWindow.xaml             # Головне вікно
    └── MainWindow.xaml.cs
```

### Потік даних

```
Мережевий пакет
    ↓
PacketInterceptor (перехоплення)
    ↓
FilterEngine (перевірка правил)
    ↓
┌───────────────┬───────────────┬────────────────┐
│    ALLOW      │    BLOCK      │    TUNNEL      │
│  (пропустити) │ (заблокувати) │ (зашифрувати)  │
└───────────────┴───────────────┴────────────────┘
                                    ↓
                        MQECryptoService (шифрування)
                                    ↓
                        SecureTunnelService (відправка)
                                    ↓
                            Віддалений вузол
```

---

## 🛠️ Технологічний стек

### Backend
- **.NET 8.0** - Сучасна платформа розробки
- **C# 12** - Остання версія мови
- **SQLite** - Легка база даних для правил
- **Serilog** - Структуроване логування

### Frontend
- **WPF (.NET 8.0-windows)** - Нативний Windows UI
- **MahApps.Metro 2.4.11** - Metro Design
- **LiveCharts.Wpf 0.9.7** - Графіки в реальному часі

### Мережа
- **Raw Sockets** - Низькорівневе перехоплення пакетів
- **TCP/IP** - Базовий транспорт для тунелю
- **SHA256** - Хешування та контрольні суми

---

## 📥 Встановлення

### Вимоги

- **Windows 10/11** (x64)
- **.NET 8.0 Runtime** ([завантажити](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Права адміністратора** (для перехоплення пакетів)

### Збірка з вихідного коду

```bash
# Клонувати репозиторій
git clone https://github.com/yourusername/BlackCat.git
cd BlackCat

# Відновити залежності
dotnet restore

# Зібрати всі проекти
dotnet build --configuration Release

# Запустити UI
cd BlackCat.UI
dotnet run

# АБО встановити як Windows Service
cd BlackCat.Service
dotnet publish -c Release -r win-x64 --self-contained
sc create BlackCatFirewall binPath="C:\Path\To\BlackCat.Service.exe"
sc start BlackCatFirewall
```

---

## 🚀 Використання

### 1. Запуск UI

```bash
cd BlackCat.UI
dotnet run
```

**Важливо:** Запустіть з правами адміністратора для перехоплення пакетів!

```powershell
# PowerShell (як адміністратор)
Start-Process "BlackCat.UI.exe" -Verb RunAs
```

### 2. Налаштування правил

#### Приклад правил фільтрації

```csharp
// Дозволити локальну мережу
new FilterRule
{
    Name = "Дозволити локальну мережу",
    IPAddress = "192.168.0.0/16",
    Action = FilterAction.Allow,
    Direction = TrafficDirection.Both
}

// Заблокувати підозрілий IP
new FilterRule
{
    Name = "Блокувати підозрілий сервер",
    IPAddress = "123.45.67.89",
    Port = 0,
    Protocol = ProtocolType.Any,
    Action = FilterAction.Block,
    Direction = TrafficDirection.Outbound,
    Priority = 1  // Вищий пріоритет
}

// Направити через тунель
new FilterRule
{
    Name = "Захищений тунель для SSH",
    Port = 22,
    Protocol = ProtocolType.TCP,
    Action = FilterAction.Tunnel,
    Direction = TrafficDirection.Outbound
}
```

### 3. Конфігурація тунелю

**appsettings.json:**

```json
{
  "BlackCat": {
    "MasterSecret": "YourSecure256BitSecretKeyHere!",
    "DatabasePath": "blackcat.db",
    "TunnelPort": 9999,
    "DefaultAllow": false,
    "EnablePacketInterception": true
  }
}
```

**⚠️ ВАЖЛИВО:** Змініть `MasterSecret` на унікальний пароль!

---

## 🔐 Криптографія MQE

### Modular Quaternion Encryption (MQE)

BlackCat використовує власний алгоритм шифрування на основі кватерніонів.

#### Що таке кватерніони?

Кватерніон - це розширення комплексних чисел у 4-вимірному просторі:

```
Q = w + xi + yj + zk
```

де `i² = j² = k² = ijk = -1`

#### Алгоритм шифрування

**1. Генерація ключа:**

```csharp
string rawKey = MasterSecret + Timestamp;
byte[] hash = SHA256(rawKey);
Quaternion K = Quaternion(hash[0], hash[1], hash[2], hash[3]);
```

**2. Шифрування блоку (4 байти):**

```csharp
Quaternion Data = Quaternion(b1, b2, b3, b4);
Quaternion Cipher = Data * K;  // Множення Гамільтона
byte[] encrypted = Cipher.Normalize();  // Модуль 256
```

**3. Розшифрування:**

```csharp
Quaternion K_inverse = K.ModularInverse(256);
Quaternion Original = Cipher * K_inverse;
byte[] decrypted = Original.Normalize();
```

#### Переваги MQE

✅ **Стійкість до частотного аналізу** - Нелінійне перетворення
✅ **Динамічний ключ** - Змінюється кожного разу
✅ **Захист від Replay** - Перевірка Timestamp
✅ **Цілісність даних** - SHA256 checksum
✅ **Швидкість** - Тільки цілочисельна арифметика

---

## ⚙️ Конфігурація

### Вимоги до системи (NFR)

| Параметр | Значення | Примітки |
|----------|----------|----------|
| Затримка (Latency) | < 50 мс | Вплив шифрування на пакет |
| Пропускна здатність | 1 Gbps | Обмеження hardware |
| Timestamp Validity | 5 секунд | Захист від Replay Attack |
| Розмір блоку | 4 байти | Один кватерніон |
| Модуль | 256 | Для байтового діапазону |

### Правила фільтрації за замовчуванням

```sql
-- За замовчуванням - БЛОКУВАТИ ВСЕ
DefaultAllow = FALSE

-- Дозволити localhost
ALLOW 127.0.0.1/8 (Priority: 1)

-- Дозволити локальну мережу
ALLOW 192.168.0.0/16 (Priority: 10)
ALLOW 10.0.0.0/8 (Priority: 10)
ALLOW 172.16.0.0/12 (Priority: 10)
```

---

## 🧪 Розробка

### Запуск тестів

```bash
# TODO: Додати unit-тести
dotnet test
```

### Архітектурні патерни

- **Repository Pattern** - RuleRepository для SQLite
- **Service Layer** - FilterEngine, SecureTunnelService
- **Observer Pattern** - Події для перехоплення та фільтрації
- **Coordinator Pattern** - FirewallCoordinator об'єднує все

### Додавання нових правил програмно

```csharp
var repository = new RuleRepository();

var rule = new FilterRule
{
    Name = "Моє правило",
    IPAddress = "203.0.113.0/24",
    Port = 443,
    Protocol = ProtocolType.TCP,
    Action = FilterAction.Tunnel,
    Direction = TrafficDirection.Outbound,
    Priority = 50
};

int id = repository.AddRule(rule);
coordinator.LoadRules();  // Перезавантажити
```

---

## 📊 Статистика та моніторинг

### Метрики

| Метрика | Опис |
|---------|------|
| TotalPackets | Загальна кількість оброблених пакетів |
| AllowedPackets | Дозволені пакети |
| BlockedPackets | Заблоковані пакети |
| TunneledPackets | Відправлені через захищений тунель |
| ErrorPackets | Пакети з помилками |
| BytesPerSecond | Швидкість потоку (байт/сек) |
| AverageLatencyMs | Середня затримка шифрування (мс) |
| TunnelStatus | Статус з'єднання тунелю |

### Логування

Логи зберігаються в `logs/blackcat-{date}.log` з ротацією по днях.

```
[2026-01-13 15:30:45.123] [INF] BlackCat Firewall Service запущено
[2026-01-13 15:30:46.456] [INF] 🚫 Заблоковано: 192.168.1.100:54321 → 203.0.113.1:80
[2026-01-13 15:30:47.789] [INF] 🔒 Відправлено через тунель: 10.0.0.5:443
```

---

## 🐛 Відомі обмеження

1. **Перехоплення пакетів** - Вимагає прав адміністратора та працює тільки на Windows
2. **Raw Sockets** - Обмежена підтримка в деяких версіях Windows (потрібен WinPcap/Npcap для повної функціональності)
3. **Application Layer** - Поточна реалізація працює на рівні додатку, для драйверного рівня потрібен WFP (Windows Filtering Platform)

---

## 🔮 Roadmap

- [ ] Інтеграція з WinPcap/Npcap для повного перехоплення
- [ ] Драйвер WFP для kernel-level фільтрації
- [ ] Web Dashboard (ASP.NET Core)
- [ ] REST API для керування
- [ ] Multi-platform support (Linux через iptables)
- [ ] Advanced analytics з ML
- [ ] Performance benchmarks vs AES-256

---

## 🤝 Внесок

Вітаються pull requests! Для великих змін спочатку відкрийте issue для обговорення.

---

## 📄 Ліцензія

MIT License - See [LICENSE](LICENSE) file

---

## ✍️ Автор

**Розроблено для вирішення критичних проблем безпеки мережевої комунікації в проекті DCS.**

**BlackCat Firewall** © 2026

---

## 🙏 Подяки

- **Quaternion Mathematics** - William Rowan Hamilton
- **MahApps.Metro** - Modern WPF UI
- **LiveCharts** - Beautiful charts for WPF
- **Serilog** - Structured logging

---

## 📞 Підтримка

Якщо у вас виникли питання або проблеми:

1. Перевірте [Issues](https://github.com/yourusername/BlackCat/issues)
2. Створіть новий Issue з детальним описом
3. Переконайтеся, що програма запущена з правами адміністратора

---

**⚠️ DISCLAIMER:**
Цей програмний продукт призначений тільки для легального використання в захищених мережах.
Не використовуйте для несанкціонованого перехоплення трафіку.
