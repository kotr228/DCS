# BlackCat — Архітектура системи

## Загальний огляд

BlackCat складається з шести C#-проектів з чіткою відповідальністю кожного шару:

```
┌─────────────────────────────────────────┐
│            BlackCat.UI (WPF)            │  ← Інтерфейс, події, P2P signaling
├─────────────────────────────────────────┤
│           BlackCat.Core                 │  ← Бізнес-логіка, БД, сервіси
├──────────────────┬──────────────────────┤
│ BlackCat.Network │   BlackCat.Crypto    │  ← Мережа та шифрування
│       Core       │   (MQE, Quaternion)  │
├──────────────────┴──────────────────────┤
│           BlackCat.Shared               │  ← Моделі, enum-и
└─────────────────────────────────────────┘
              BlackCat.Service             ← Windows Service host
```

---

## Проекти

### BlackCat.Shared

Загальні контракти даних без залежностей.

```
Models/
  BlackID.cs          — ідентифікатор вузла (РОЛЬ-МІСТО-НАЗВА-КОД)
  PeerNode.cs         — запис телефонної книги
  TunnelPacket.cs     — зашифрований UDP-пакет (Version, Timestamp,
                        EncryptedPayload, Checksum, SourceIP, DestinationIP)
  FilterRule.cs       — правило брандмауера
  PacketInfo.cs       — мережевий пакет (src/dst IP, port, protocol, payload)
  ConnectionEvent.cs  — подія журналу (тип, напрямок, IP, байти)
  FirewallStatistics.cs

Enums/
  ProtocolType        — TCP, UDP, ICMP, Any
  FilterAction        — Allow, Block, Tunnel
  TrafficDirection    — Inbound, Outbound, Both
  TunnelStatus        — Disconnected, Connecting, Connected, Error
```

**TunnelPacket серіалізація:**
```
[1B Version][8B Timestamp][4B PayloadLen][Payload][4B ChecksumLen][Checksum]
```
`SourceIP` і `DestinationIP` не серіалізуються — вони відомі з UDP-заголовка.

---

### BlackCat.Crypto

Реалізація MQE без зовнішніх залежностей.

#### Quaternion.cs

Структура кватерніона в кільці Z_256:

```csharp
struct Quaternion { int W, X, Y, Z; }
```

**Операції:**

| Метод | Формула |
|-------|---------|
| `operator *` | Множення Гамільтона mod 256 |
| `Norm()` | W²+X²+Y²+Z² |
| `ModularInverse(m)` | K⁻¹ такий що K×K⁻¹ ≡ I (mod m) |
| `ToBytes()` | [W mod 256, X mod 256, Y mod 256, Z mod 256] |
| `FromBytes()` | Створення з чотирьох байтів |

**Умова оборотності:** норма ключа повинна бути непарною. Якщо парна — `W += 1`.

#### MQECryptoService.cs

```csharp
TunnelPacket Encrypt(byte[] data, string srcIP, string dstIP)
byte[]        Decrypt(TunnelPacket packet)
```

**Encrypt:**
1. `timestamp = DateTime.UtcNow.Ticks`
2. `key = GenerateSessionKey(timestamp)` — SHA256(secret+timestamp) → перші 4 байти
3. Додати PKCS7-паддінг до кратності 4
4. Для кожного 4-байтного блоку: `cipher = data_block * key` (мод 256)
5. Обчислити SHA256 зашифрованих даних
6. Повернути `TunnelPacket { Version=1, Timestamp, EncryptedPayload, Checksum }`

**Decrypt:**
1. Перевірити `|currentTime - packet.Timestamp| <= 30 секунд`
2. Перевірити SHA256 контрольну суму
3. `key = GenerateSessionKey(packet.Timestamp)`
4. `inverseKey = key.ModularInverse(256)`
5. Для кожного блоку: `plain = cipher_block * inverseKey`
6. Видалити PKCS7-паддінг

**Константи:**
```csharp
BLOCK_SIZE               = 4   // байтів (1 кватерніон)
MODULUS                  = 256 // кільце Z_256
TIMESTAMP_VALIDITY_SECONDS = 30 // захист від replay
```

---

### BlackCat.NetworkCore

#### PacketInterceptor.cs

Перехоплення IP-пакетів через Raw Socket:
- Відкриває `Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP)`
- `SetSocketOption(IP_HDRINCL)` — отримувати заголовки IP
- `IOControl(SIO_RCVALL)` — отримувати весь трафік мережевого інтерфейсу
- Для кожного пакету викликає `PacketReceived` з `PacketInfo`

#### SecureTunnelService.cs

TCP-сервер і клієнт для захищеного з'єднання (порт 9999 за замовчуванням).

**Handshake протокол:**
```
Client -> Server: HELLO (наш BlackID, PublicKey)
Server -> Client: CHALLENGE (нонс, підписаний нашим ключем)
Client -> Server: RESPONSE (підпис нонсу нашим приватним ключем)
Server -> Client: ACCEPT (SessionId) або REJECT (причина)
```

#### UdpEncryptedTunnel.cs

Легкий UDP-тунель для P2P після hole punching.

```csharp
Task SendAsync(byte[] plaintext, string srcIp = "", string dstIp = "")
```

**Потоки:**
- `ReceiveLoopAsync` — читає UDP, якщо `0xFF` → keepalive (ігнорується), інакше `TunnelPacket.FromBytes()` + `Decrypt()`
- `KeepaliveLoopAsync` — кожні 5 секунд надсилає `{ 0xFF }` (raw, без MQE); якщо `_lastSeen` > 30с → `Disconnected`

Keepalive `0xFF` не шифрується. Зашифровані пакети починаються з байту Version (`0x01`), тому їх легко відрізнити.

#### StunClient.cs

Запит до публічного STUN-сервера (stun.l.google.com:19302) для визначення зовнішнього IP:порту через RFC 5389.

---

### BlackCat.Core

#### FilterEngine.cs

Зіставлення пакетів з правилами:
- Правила відсортовані за пріоритетом (вищий пріоритет = перевіряється першим)
- Підтримка CIDR нотації для IP (наприклад `192.168.1.0/24`)
- При збігу повертає `FilterAction` (Allow, Block, Tunnel) + ціль тунелю

#### FirewallCoordinator.cs

Центральний координатор:
- Запускає `PacketInterceptor` і `TunnelManager`
- Для кожного перехопленого пакету → `FilterEngine.Check()` → дія
- Агрегує статистику (`TotalPackets`, `AllowedPackets`, `BlockedPackets`, `TunneledPackets`, `BytesPerSecond`)

#### TunnelManager.cs

Менеджер всіх P2P-з'єднань. Ключові колекції:

```csharp
ConcurrentDictionary<string, PeerTunnelConnection> _activeTunnels   // TCP
ConcurrentDictionary<string, UdpEncryptedTunnel>   _udpTunnels      // UDP P2P
ConcurrentDictionary<string, RelayVirtualConnection> _relayConnections
```

Ключ в усіх словниках — BlackID піра.

**ManualHolePunchAsync(peerBlackID, peerEndpoint, udpSocket, durationSeconds):**

```
1. Запустити паралельно:
   a. Кожні 200ms надсилати {0xBC, 0xAA} на peerEndpoint
   b. Слухати відповіді на тому ж UdpClient
2. Якщо отримано {0xBC, 0xAA} від будь-якого endpoint:
   -> answeredEp = sender endpoint
   -> Зупинити punch loop
3. Створити UdpEncryptedTunnel(udpSocket, answeredEp, masterSecret)
4. Підписатись на DataReceived -> DataReceived event
5. Підписатись на Disconnected -> ConnectionLost event + видалити з _udpTunnels
6. tunnel.Start()
7. _udpTunnels[peerBlackID] = tunnel
8. Викликати ConnectionEstablished event
```

**SendDataViaRelayAsync(peerBlackID, data):**

```
if _udpTunnels[peerBlackID] exists and IsConnected:
    -> tunnel.SendAsync(data)                  // прямий UDP (пріоритет)
elif _activeTunnels[peerBlackID] exists:
    -> encrypt + write to TCP stream           // TCP fallback
elif _relayConnections[peerBlackID] exists:
    -> relayClient.SendData(peerBlackID, data) // relay
else:
    -> return false
```

**RenameUdpTunnel(oldKey, newKey):**

При отриманні реального BlackID від піра — тунель перереєструється під новим ключем:
```csharp
if _udpTunnels.TryRemove(oldKey, out var tunnel):
    _udpTunnels[newKey] = tunnel
```

#### Data Layer

**Схема SQLite (ключові таблиці):**

```sql
PeerNodes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    BlackID TEXT NOT NULL UNIQUE,
    Address TEXT NOT NULL,
    Port INTEGER NOT NULL DEFAULT 9999,
    DisplayName TEXT NOT NULL,
    IsTrusted INTEGER DEFAULT 0,
    LastConnectedAt TEXT,
    CreatedAt TEXT NOT NULL,
    IsActive INTEGER DEFAULT 1,
    SuccessfulConnections INTEGER DEFAULT 0,
    FailedConnections INTEGER DEFAULT 0
)

ConnectionEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RemoteBlackID TEXT,
    RemoteIP TEXT NOT NULL,
    RemotePort INTEGER NOT NULL,
    InitiatorBlackID TEXT,
    TargetBlackID TEXT,
    EventTypeId INTEGER NOT NULL,
    EventType INTEGER NOT NULL,        -- enum value
    Direction INTEGER NOT NULL,        -- 0=Inbound, 1=Outbound
    Message TEXT NOT NULL,
    IsAuthenticated INTEGER DEFAULT 0,
    Timestamp TEXT NOT NULL,
    BytesSent INTEGER DEFAULT 0,
    BytesReceived INTEGER DEFAULT 0
)
```

`UpdatePeerNode` оновлює за `Id` (integer PK) — дозволяє змінити поле `BlackID` без UNIQUE конфлікту, якщо рядки мають різні Id.

---

### BlackCat.UI

#### Маркери пакетів

```csharp
NodeInfoMarker     = { 0xBC, 0x1D }  // обмін Black-ID
FileTransferMarker = { 0xBC, 0x1E }  // передача файлів
```

#### Авто-обмін інформацією про вузол

**Ініціатор** (після успішного hole punch, затримка 400ms):
```
SendNodeInfoAsync(peerBlackID)
  -> JSON: { blackID, displayName }
  -> пакет: [0xBC][0x1D] + JSON bytes
  -> SendDataViaRelayAsync(peerBlackID, packet) -- через MQE тунель
  -> зберегти JSON в temp_data/nodeinfo_sent_{ourId}.json
```

**Одержувач (HandleNodeInfoPacket):**
```
1. Розпарсити JSON -> blackID, name
2. Знайти node в _tunnelNodes де IPAddress == sourceIP
3. Якщо node.BlackID == blackID -> вже актуально, вийти
4. oldId = node.BlackID; wasTempId = oldId.StartsWith("peer_")
5. RenameUdpTunnel(oldId, blackID)
6. node.BlackID = blackID (UI оновлюється негайно)
7. Видалити дублікат якщо є інший вузол з тим же BlackID
8. DB операція в окремому try/catch:
   - Якщо blackID вже є в DB: видалити temp запис, оновити існуючий
   - Якщо тільки oldId є в DB: оновити BlackID
9. Зберегти JSON в temp_data/nodeinfo_recv_{IP}.json
10. Якщо wasTempId: відповісти своєю інформацією (уникнення циклу)
```

#### Формат файл-пакету

```
[0xBC][0x1E]           2 байти   маркер типу
[N: 4 байти LE]                  довжина імені файлу
[filename: N байтів]              ім'я (UTF-8)
[content: решта]                  вміст файлу
```

Весь пакет передається через `SendDataViaRelayAsync` — шифрується MQE в `UdpEncryptedTunnel.SendAsync`.

#### Логіка дедублікації TunnelNodes

Проблема: `_tunnelNodes` може мати запис завантажений з БД (реальний BlackID) і тимчасовий запис (peer_IP_Port). Після перейменування тимчасового запису виникає дублікат.

Рішення в двох місцях:
```csharp
// 1. P2PConnectButton_Click — після визначення реального BlackID з БД
var dup = _tunnelNodes.FirstOrDefault(t => t != existingItem && t.BlackID == existingItem.BlackID);
if (dup != null) _tunnelNodes.Remove(dup);

// 2. HandleNodeInfoPacket — після node.BlackID = blackID
var dupNode = _tunnelNodes.FirstOrDefault(t => t != node && t.BlackID == blackID);
if (dupNode != null) _tunnelNodes.Remove(dupNode);
```

---

## Послідовність підключення

```
Машина A                                    Машина B
    |                                            |
    |-- P2PGetCodeButton_Click                   |
    |     -> StunClient.GetPublicEndpointAsync   |
    |     -> Показати "81.162.255.205:54321"     |
    |                                            |
    |   [Користувачі обмінюються кодами]         |
    |                                            |
    |-- P2PConnectButton_Click                   |-- P2PConnectButton_Click
    |     -> ManualHolePunchAsync                |     -> ManualHolePunchAsync
    |          -> {0xBC,0xAA} ------------------>|
    |          <---------------------- {0xBC,0xAA}|
    |                                            |
    |   [NAT відкрито з обох сторін]             |
    |                                            |
    |-- UdpEncryptedTunnel.Start()               |-- UdpEncryptedTunnel.Start()
    |-- ConnectionEstablished event              |-- ConnectionEstablished event
    |     -> UI: Підключено                      |     -> UI: Підключено
    |     -> LogEvent(Connected) -> DB           |     -> LogEvent(Connected) -> DB
    |     -> Task.Delay(1000ms)                  |     -> Task.Delay(1000ms)
    |     -> SendHelloFileAsync                  |     -> SendHelloFileAsync
    |                                            |
    |-- Task.Delay(400ms)                        |-- Task.Delay(400ms)
    |-- SendNodeInfoAsync                        |-- SendNodeInfoAsync
    |     -> [0xBC,0x1D]+JSON -> MQE ----------->|
    |     <----------- MQE+[0xBC,0x1D]+JSON -----|
    |                                            |
    |-- HandleNodeInfoPacket                     |-- HandleNodeInfoPacket
    |     -> RenameUdpTunnel                     |     -> RenameUdpTunnel
    |     -> node.BlackID = realID               |     -> node.BlackID = realID
    |     -> DB update                           |     -> DB update
    |                                            |
    |-- HandleFilePacket                         |-- HandleFilePacket
    |     -> temp_files/hello_*.txt              |     -> temp_files/hello_*.txt
    |     -> LogEvent(FileTransfer) -> DB        |     -> LogEvent(FileTransfer) -> DB
```

---

## Події TunnelManager

| Подія | Коли | Обробник в UI |
|-------|------|---------------|
| `ConnectionEstablished` | Hole punch успішний або TCP handshake пройшов | `OnTunnelConnectionEstablished` |
| `ConnectionLost` | Keepalive timeout або явне відключення | `OnTunnelConnectionLost` |
| `ConnectionFailed` | Handshake відхилено | `OnTunnelConnectionFailed` |
| `DataReceived` | Зашифрований UDP/TCP пакет отримано | `OnTunnelDataReceived` |
| `IncomingConnectionRequest` | Вхідний TCP handshake | `OnIncomingConnectionRequest` |
| `UPnPStatusChanged` | UPnP відкрив/не відкрив порт | `OnUPnPStatusChanged` |
| `NatDiagnosticReady` | Діагностика мережі завершена | `OnNatDiagnosticReady` |

---

## Технологічний стек

| Компонент | Технологія |
|-----------|------------|
| UI | WPF (.NET 8.0-windows), LiveCharts.Wpf |
| База даних | SQLite (Microsoft.Data.Sqlite) |
| Шифрування | MQE (власний) + SHA256 (System.Security.Cryptography) |
| Мережа | Raw Socket, UdpClient, TcpClient |
| STUN | RFC 5389 (stun.l.google.com:19302) |
| NAT | UPnP або ручне port forwarding |
| Логування | Serilog з файловою ротацією |
| Windows Service | .NET Generic Host + UseWindowsService() |
