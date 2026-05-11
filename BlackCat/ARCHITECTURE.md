# Архітектура BlackCat Firewall

## Огляд системи

BlackCat Firewall - це модульний брандмауер, який складається з 6 основних компонентів:

```
┌──────────────────────────────────────────────────┐
│              BlackCat.UI (WPF)                   │
│         Візуалізація та керування                │
└─────────────────┬────────────────────────────────┘
                  │
┌─────────────────▼────────────────────────────────┐
│        BlackCat.Service (Windows Service)        │
│             Фонова служба системи                │
└─────────────────┬────────────────────────────────┘
                  │
┌─────────────────▼────────────────────────────────┐
│       BlackCat.Core (Бізнес-логіка)              │
│  ┌──────────────────────────────────────────┐    │
│  │     FirewallCoordinator                  │    │
│  │  (координація всіх компонентів)         │    │
│  └──────────────────────────────────────────┘    │
│           ↓             ↓             ↓           │
│    ┌──────────┐  ┌──────────┐  ┌──────────┐     │
│    │ Filter   │  │   Rule   │  │Statistics│     │
│    │ Engine   │  │Repository│  │ Monitor  │     │
│    └──────────┘  └──────────┘  └──────────┘     │
└────────────┬─────────────────────────┬───────────┘
             │                         │
┌────────────▼─────────┐     ┌────────▼───────────┐
│ BlackCat.NetworkCore │     │  BlackCat.Crypto   │
│  ┌────────────────┐  │     │  ┌──────────────┐  │
│  │ Packet         │  │     │  │ Quaternion   │  │
│  │ Interceptor    │  │     │  │              │  │
│  └────────────────┘  │     │  └──────────────┘  │
│  ┌────────────────┐  │     │  ┌──────────────┐  │
│  │ Secure         │◄─┼─────┼──┤ MQE Crypto   │  │
│  │ Tunnel         │  │     │  │ Service      │  │
│  └────────────────┘  │     │  └──────────────┘  │
└──────────────────────┘     └────────────────────┘
           ↓                          ↑
           │                          │
      ┌────▼──────────────────────────┴────┐
      │        Мережа (TCP/IP)             │
      └────────────────────────────────────┘
```

---

## Детальний опис компонентів

### 1. BlackCat.Shared (Спільні моделі)

#### Призначення
Містить спільні моделі даних та енуми, які використовуються всіма проектами.

#### Основні класи:

**PacketInfo:**
```csharp
public class PacketInfo
{
    string SourceIP           // IP джерела
    string DestinationIP      // IP призначення
    int SourcePort            // Порт джерела
    int DestinationPort       // Порт призначення
    ProtocolType Protocol     // TCP/UDP/ICMP
    byte[] Payload            // Дані пакету
    DateTime Timestamp        // Час створення
    bool IsTunnelPacket       // Чи це пакет тунелю
}
```

**FilterRule:**
```csharp
public class FilterRule
{
    int Id                    // Унікальний ID
    string Name               // Назва правила
    string IPAddress          // IP або підмережа (CIDR)
    int Port                  // Порт (0 = будь-який)
    ProtocolType Protocol     // Протокол
    FilterAction Action       // Allow/Block/Tunnel
    TrafficDirection Direction // Inbound/Outbound/Both
    bool IsEnabled            // Активне чи ні
    int Priority              // Пріоритет (нижчий = вищий)
}
```

**TunnelPacket:**
```csharp
public class TunnelPacket
{
    byte Version              // Версія протоколу (1)
    long Timestamp            // Часова мітка для ключа
    byte[] EncryptedPayload   // Зашифровані дані
    byte[] Checksum           // SHA256 контрольна сума
    string SourceIP           // IP джерела (незашифрований)
    string DestinationIP      // IP призначення (незашифрований)
}
```

---

### 2. BlackCat.Crypto (Криптографія)

#### Quaternion (Кватерніон)

**Структура:**
```csharp
public struct Quaternion
{
    int W, X, Y, Z  // Компоненти кватерніона
}
```

**Ключові операції:**

1. **Множення Гамільтона:**
```csharp
q1 * q2 = (
    w: q1.W * q2.W - q1.X * q2.X - q1.Y * q2.Y - q1.Z * q2.Z,
    x: q1.W * q2.X + q1.X * q2.W + q1.Y * q2.Z - q1.Z * q2.Y,
    y: q1.W * q2.Y - q1.X * q2.Z + q1.Y * q2.W + q1.Z * q2.X,
    z: q1.W * q2.Z + q1.X * q2.Y - q1.Y * q2.X + q1.Z * q2.W
)
```

2. **Норма:**
```csharp
Norm(q) = W² + X² + Y² + Z²
```

3. **Спряжений:**
```csharp
Conjugate(q) = (W, -X, -Y, -Z)
```

4. **Обернений за модулем:**
```csharp
q⁻¹ = Conjugate(q) * ModInverse(Norm(q), 256)
```

#### MQECryptoService

**Алгоритм шифрування (повний):**

```
INPUT: data (byte[]), masterSecret (string)

1. Генерація ключа:
   timestamp = DateTime.UtcNow.Ticks
   rawKey = masterSecret + timestamp
   hash = SHA256(rawKey)
   K = Quaternion(hash[0], hash[1], hash[2], hash[3])

   IF Norm(K) % 2 == 0:
       K.W += 1  // Гарантуємо непарну норму (оборотність)

2. Padding (PKCS7):
   paddingSize = 4 - (data.Length % 4)
   paddedData = data + [paddingSize, paddingSize, ...]

3. Шифрування блоками:
   FOR кожен блок B (4 байти):
       D = Quaternion(B[0], B[1], B[2], B[3])
       C = D * K  // Множення Гамільтона
       encryptedBlock = C.Normalize()  // % 256
       APPEND encryptedBlock TO result

4. Обчислення checksum:
   checksum = SHA256(encryptedData)

5. Створення TunnelPacket:
   packet = {
       Version: 1,
       Timestamp: timestamp,
       EncryptedPayload: encryptedData,
       Checksum: checksum,
       SourceIP: sourceIP,
       DestinationIP: destIP
   }

OUTPUT: packet
```

**Алгоритм розшифрування:**

```
INPUT: packet (TunnelPacket)

1. Валідація timestamp:
   currentTime = DateTime.UtcNow.Ticks
   timeDiff = |currentTime - packet.Timestamp|
   IF timeDiff > 5 секунд:
       THROW "Replay Attack або застарілий пакет"

2. Валідація checksum:
   expectedChecksum = SHA256(packet.EncryptedPayload)
   IF expectedChecksum != packet.Checksum:
       THROW "Пакет пошкоджено"

3. Відновлення ключа:
   rawKey = masterSecret + packet.Timestamp
   hash = SHA256(rawKey)
   K = Quaternion(hash[0], hash[1], hash[2], hash[3])
   IF Norm(K) % 2 == 0: K.W += 1

4. Обчислення оберненого ключа:
   K⁻¹ = K.ModularInverse(256)

5. Розшифрування блоками:
   FOR кожен блок C (4 байти):
       Cipher = Quaternion(C[0], C[1], C[2], C[3])
       D = Cipher * K⁻¹  // Множення Гамільтона
       decryptedBlock = D.Normalize()  // % 256
       APPEND decryptedBlock TO result

6. Видалення padding:
   paddingSize = result[result.Length - 1]
   data = result[0..result.Length - paddingSize]

OUTPUT: data
```

**Математичне обґрунтування:**

```
Шифрування: C = D * K
Розшифрування: D' = C * K⁻¹
            D' = (D * K) * K⁻¹
            D' = D * (K * K⁻¹)
            D' = D * I  (де I - одиничний кватерніон)
            D' = D ✓
```

---

### 3. BlackCat.NetworkCore (Мережевий слой)

#### PacketInterceptor

**Принцип роботи:**

1. Створює Raw Socket з типом `ProtocolType.IP`
2. Прив'язується до локального мережевого інтерфейсу
3. Використовує `IOControl(ReceiveAll)` для отримання всіх IP пакетів
4. Парсить IP заголовок та витягує:
   - Версія IP
   - Протокол (TCP/UDP/ICMP)
   - IP джерела та призначення
   - Порти (для TCP/UDP)
   - Payload

**IP пакет структура:**

```
0                   1                   2                   3
0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|Version|  IHL  |Type of Service|          Total Length         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|         Identification        |Flags|      Fragment Offset    |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|  Time to Live |    Protocol   |         Header Checksum       |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                       Source Address                          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                    Destination Address                        |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                    Options (variable)                         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                      Data (Payload)                           |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

#### SecureTunnelService

**Протокол передачі:**

```
1. Клієнт підключається до сервера через TCP
2. Відправляє:
   [4 байти - Розмір пакету]
   [N байтів - TunnelPacket]

Структура TunnelPacket:
┌────────┬───────────┬────────────┬───────────┐
│Version │ Timestamp │  Payload   │ Checksum  │
│(1 byte)│ (8 bytes) │ (variable) │(32 bytes) │
└────────┴───────────┴────────────┴───────────┘
```

**Обробка з'єднань:**

```
Server:
  ├─ TcpListener.Start() на порту 9999
  ├─ AcceptClientsAsync() (безкінечний цикл)
  │   ├─ AcceptTcpClientAsync()
  │   └─ HandleClientAsync() (окреме Task)
  │       ├─ Читати розмір пакету (4 байти)
  │       ├─ Читати пакет (N байтів)
  │       ├─ Десеріалізувати TunnelPacket
  │       ├─ Розшифрувати MQECryptoService
  │       └─ PacketReceived event

Client:
  ├─ TcpClient.Connect(remoteIP, remotePort)
  ├─ Зашифрувати дані
  ├─ Серіалізувати TunnelPacket
  ├─ Відправити [розмір + пакет]
  └─ Flush
```

---

### 4. BlackCat.Core (Бізнес-логіка)

#### FilterEngine

**Алгоритм перевірки пакету:**

```
INPUT: packet (PacketInfo), direction (TrafficDirection)

rules = GetActiveRulesSortedByPriority()

FOR EACH rule IN rules:
    // Перевірка напрямку
    IF rule.Direction != Both AND rule.Direction != direction:
        CONTINUE

    // Перевірка протоколу
    IF rule.Protocol != Any AND rule.Protocol != packet.Protocol:
        CONTINUE

    // Перевірка IP
    IF rule.IPAddress != empty:
        IF rule.IPAddress contains '/':  // CIDR
            matches = IsIPInSubnet(packet.SourceIP, rule.IPAddress) OR
                     IsIPInSubnet(packet.DestinationIP, rule.IPAddress)
        ELSE:
            matches = packet.SourceIP == rule.IPAddress OR
                     packet.DestinationIP == rule.IPAddress

        IF NOT matches:
            CONTINUE

    // Перевірка порту
    IF rule.Port != 0:
        IF packet.SourcePort != rule.Port AND packet.DestinationPort != rule.Port:
            CONTINUE

    // Правило підійшло!
    RETURN FilterDecision{Action: rule.Action, MatchedRule: rule}

// Жодне правило не підійшло
RETURN FilterDecision{Action: DefaultAllow ? Allow : Block}
```

**CIDR перевірка:**

```
INPUT: ipAddress (string), cidr (string)  // "192.168.1.100", "192.168.1.0/24"

1. Розібрати CIDR:
   parts = cidr.Split('/')
   network = IPAddress.Parse(parts[0])
   prefixLength = int.Parse(parts[1])  // 24

2. Конвертувати IP в байти:
   ipBytes = IPAddress.Parse(ipAddress).GetAddressBytes()
   networkBytes = network.GetAddressBytes()

3. Застосувати маску:
   maskBits = prefixLength  // 24
   FOR i = 0 TO 3:
       mask = maskBits >= 8 ? 255 :
              maskBits > 0 ? (255 << (8 - maskBits)) : 0

       IF (ipBytes[i] & mask) != (networkBytes[i] & mask):
           RETURN false

       maskBits -= 8

4. RETURN true
```

#### FirewallCoordinator

**Головний цикл обробки:**

```
START:
  ├─ Ініціалізація компонентів
  │   ├─ PacketInterceptor
  │   ├─ FilterEngine
  │   ├─ SecureTunnelService
  │   └─ RuleRepository
  │
  ├─ Завантаження правил з БД
  │
  ├─ Запуск сервісів
  │   ├─ SecureTunnelService.StartAsync()
  │   └─ PacketInterceptor.Start()
  │
  └─ Основний цикл:
      │
      ┌──> PacketCaptured event
      │    ├─ DetermineDirection(packet)
      │    ├─ FilterEngine.CheckPacket(packet, direction)
      │    │
      │    └─ Switch (decision.Action):
      │        ├─ Allow  → Statistics.AllowedPackets++
      │        ├─ Block  → Statistics.BlockedPackets++ + Log
      │        └─ Tunnel → MQECryptoService.Encrypt()
      │                    → SecureTunnelService.SendAsync()
      │                    → Statistics.TunneledPackets++
      │
      └──> StatisticsMonitor (кожні 1 сек)
           ├─ Обчислити BytesPerSecond
           ├─ Обчислити AverageLatency
           └─ StatisticsUpdated event
```

---

### 5. BlackCat.Service (Windows Service)

**Життєвий цикл:**

```
1. Program.Main():
   ├─ Налаштувати Serilog
   ├─ CreateHostBuilder()
   │   ├─ UseWindowsService()
   │   └─ AddHostedService<BlackCatWorker>()
   └─ host.Run()

2. BlackCatWorker.ExecuteAsync():
   ├─ Читати конфігурацію (appsettings.json)
   │   ├─ MasterSecret
   │   ├─ DatabasePath
   │   └─ TunnelPort
   │
   ├─ Створити FirewallCoordinator
   ├─ Підписатися на події
   │   ├─ LogMessage → Serilog.Log
   │   └─ StatisticsUpdated → Періодичний лог
   │
   ├─ coordinator.StartAsync()
   └─ await Task.Delay(Timeout.Infinite)  // Чекати зупинки

3. BlackCatWorker.StopAsync():
   ├─ coordinator.Stop()
   └─ coordinator.Dispose()
```

---

### 6. BlackCat.UI (WPF інтерфейс)

**Архітектура UI:**

```
MainWindow
  ├─ Статистика (Grid)
  │   ├─ TotalPackets
  │   ├─ AllowedPackets
  │   ├─ BlockedPackets
  │   ├─ TunneledPackets
  │   └─ Speed (KB/s)
  │
  ├─ TabControl
  │   ├─ Tab "Графіки"
  │   │   ├─ LiveCharts.CartesianChart (пакети)
  │   │   ├─ Статус тунелю (індикатор)
  │   │   └─ Uptime
  │   │
  │   ├─ Tab "Правила"
  │   │   ├─ Button "Додати правило"
  │   │   └─ DataGrid (правила з БД)
  │   │
  │   └─ Tab "Лог"
  │       ├─ Button "Очистити"
  │       └─ TextBox (логи в реальному часі)
  │
  └─ Кнопки керування
      ├─ Запустити
      ├─ Зупинити
      └─ Налаштування
```

**Оновлення UI:**

```
DispatcherTimer (500 мс):
  ├─ Читати coordinator.Statistics
  ├─ Оновити текстові блоки
  ├─ Додати точки на графіки
  │   ├─ _allowedValues.Add(stats.AllowedPackets)
  │   ├─ _blockedValues.Add(stats.BlockedPackets)
  │   └─ _tunneledValues.Add(stats.TunneledPackets)
  │
  └─ Обмежити кількість точок (max 50)
```

---

## Потоки даних

### Сценарій 1: Дозволений пакет

```
1. Мережа → PacketInterceptor
2. PacketInterceptor → PacketInfo
3. PacketInfo → FilterEngine.CheckPacket()
4. FilterEngine → FilterDecision{Action: Allow}
5. Statistics.AllowedPackets++
6. Пакет пропущено → Мережа
```

### Сценарій 2: Заблокований пакет

```
1. Мережа → PacketInterceptor
2. PacketInterceptor → PacketInfo
3. PacketInfo → FilterEngine.CheckPacket()
4. FilterEngine → FilterDecision{Action: Block}
5. Statistics.BlockedPackets++
6. Serilog.Log("Заблоковано: 192.168.1.100 → 203.0.113.1")
7. Пакет відхилено (DROP)
```

### Сценарій 3: Тунелювання

```
1. Мережа → PacketInterceptor
2. PacketInterceptor → PacketInfo
3. PacketInfo → FilterEngine.CheckPacket()
4. FilterEngine → FilterDecision{Action: Tunnel}
5. PacketInfo.Payload → MQECryptoService.Encrypt()
   ├─ Генерація ключа (Timestamp)
   ├─ Блокове шифрування (кватерніони)
   └─ TunnelPacket
6. TunnelPacket → SecureTunnelService.SendAsync()
7. TCP Socket → Віддалений вузол
8. Statistics.TunneledPackets++
```

### Сценарій 4: Прийом з тунелю

```
1. Віддалений вузол → TCP Socket
2. SecureTunnelService.HandleClientAsync()
3. Читання [розмір + TunnelPacket]
4. TunnelPacket.FromBytes()
5. MQECryptoService.Decrypt()
   ├─ Валідація Timestamp (< 5 сек)
   ├─ Валідація Checksum (SHA256)
   ├─ Відновлення ключа
   ├─ Обчислення K⁻¹
   └─ Розшифрування блоками
6. Plaintext → PacketReceived event
7. Serilog.Log("Отримано з тунелю: 10.0.0.5 → 192.168.1.10")
```

---

## Безпека

### Загрози та захист

| Загроза | Захист |
|---------|--------|
| **Replay Attack** | Валідація Timestamp (5 секунд) |
| **Man-in-the-Middle** | Кватерніонне шифрування + Checksum |
| **Частотний аналіз** | Динамічний ключ для кожного пакету |
| **Brute Force** | 256⁴ = 4,294,967,296 комбінацій на блок |
| **Підробка пакету** | SHA256 Checksum |
| **Denial of Service** | Валідація розміру пакету, timeout |

### Криптографічна стійкість

**Простір ключів:**
- 1 кватерніон = 4 байти = 32 біти
- Ключ генерується з SHA256(MasterSecret + Timestamp)
- Ефективна довжина ключа: 256 біт (SHA256 hash)

**Стійкість до атак:**
- **Brute Force:** 2²⁵⁶ операцій для підбору MasterSecret
- **Known Plaintext:** Динамічний ключ для кожного пакету ускладнює аналіз
- **Chosen Plaintext:** Timestamp захищає від передобчисленних атак

---

## Продуктивність

### Часова складність

| Операція | Складність | Примітки |
|----------|-----------|----------|
| Множення кватерніонів | O(1) | 16 операцій множення/додавання |
| Генерація ключа | O(1) | SHA256 + 4 байти хешу |
| Шифрування блоку (4 б) | O(1) | 1 множення кватерніонів |
| Шифрування пакету | O(n) | n = кількість блоків |
| Перевірка правила | O(m) | m = кількість правил |
| CIDR перевірка | O(1) | 4 байти IP адреси |

### Вимоги до пам'яті

- **PacketInfo:** ~100 байт + розмір Payload
- **TunnelPacket:** ~120 байт + розмір EncryptedPayload
- **FilterRule:** ~200 байт
- **Quaternion:** 16 байт (4 × int32)

---

## Розширюваність

### Додавання нових протоколів

```csharp
// В BlackCat.Shared/Enums/ProtocolType.cs
public enum ProtocolType
{
    Any = 0,
    TCP = 6,
    UDP = 17,
    ICMP = 1,
    // Додати новий:
    SCTP = 132
}
```

### Додавання нових дій фільтрації

```csharp
// В BlackCat.Shared/Enums/FilterAction.cs
public enum FilterAction
{
    Allow,
    Block,
    Tunnel,
    // Додати нову:
    Log,       // Тільки логувати, не блокувати
    RateLimit  // Обмежити швидкість
}
```

### Інтеграція з зовнішніми системами

```csharp
// Підписатися на події
coordinator.LogMessage += (sender, message) => {
    // Відправити в Elasticsearch, Splunk, etc.
    externalLogger.Log(message);
};

coordinator.StatisticsUpdated += (sender, stats) => {
    // Відправити метрики в Prometheus, Grafana
    prometheusMetrics.UpdateGauge("firewall_packets_total", stats.TotalPackets);
};
```

---

## Висновок

BlackCat Firewall - це модульна, розширювана система з чіткою архітектурою та сильною криптографією. Кватерніонне шифрування забезпечує унікальний підхід до захисту даних, а модульна структура дозволяє легко додавати нові функції та інтегруватися з зовнішніми системами.
