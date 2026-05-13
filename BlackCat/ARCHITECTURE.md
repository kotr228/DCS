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

## База даних

SQLite файл `blackcat.db`, версія схеми **2**. Керування версіями — через `DatabaseMigrator`. При невідповідності версій міграція запускається автоматично; якщо вона не вдається — БД перестворюється з нуля через `DatabaseSchema.CreateTables`.

### Діаграма зв'язків

```
Roles ──────────────────────────────┐
Cities ──────────────────────────┐  │
                                 ↓  ↓
                           LocalBlackID

EventTypes ──────────────────────────┐
                                     ↓
                           ConnectionEvents

ConnectionStatuses ──────────────┐  ┐
                                 ↓  ↓
                             PeerNodes
                             Servers ──→ ServerLocations
                                              ↓
                                           Cities
```

---

### Таблиці довідників

#### Roles

Ролі вузлів — перша частина Black-ID (`РОЛЬ-місто-назва-код`).

```sql
CREATE TABLE Roles (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT NOT NULL UNIQUE,
    Description TEXT,
    IsActive    INTEGER DEFAULT 1,
    SortOrder   INTEGER DEFAULT 0
);
```

**Початкові дані (seeded):**

| Id | Name | Description |
|----|------|-------------|
| 1 | SKLAD | Складське приміщення |
| 2 | OFFICE | Офісне приміщення |
| 3 | MAIN | Головний вузол |
| 4 | BACKUP | Резервний вузол |
| 5 | SERVER | Серверне обладнання |
| 6 | WORKSTATION | Робоча станція |

---

#### Cities

Міста — друга частина Black-ID (`роль-МІСТО-назва-код`).

```sql
CREATE TABLE Cities (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT NOT NULL UNIQUE,
    Region          TEXT,
    Latitude        REAL,
    Longitude       REAL,
    TimezoneOffset  INTEGER,
    IsActive        INTEGER DEFAULT 1,
    SortOrder       INTEGER DEFAULT 0
);
```

**Початкові дані (seeded):**

| Id | Name | Region | Lat | Lon |
|----|------|--------|-----|-----|
| 1 | KYIV | Київська | 50.4501 | 30.5234 |
| 2 | ODESA | Одеська | 46.4825 | 30.7233 |
| 3 | LVIV | Львівська | 49.8397 | 24.0297 |
| 4 | KHARKIV | Харківська | 49.9935 | 36.2304 |
| 5 | DNIPRO | Дніпропетровська | 48.4647 | 35.0462 |
| 6 | ZAPORIZHZHIA | Запорізька | 47.8388 | 35.1396 |
| 7 | KRYVYIRIH | Дніпропетровська | 47.9102 | 33.3919 |
| 8 | MYKOLAIV | Миколаївська | 46.9750 | 32.0050 |
| 9 | MARIUPOL | Донецька | 47.0956 | 37.5489 |
| 10 | VINNYTSIA | Вінницька | 49.2328 | 28.4680 |
| 11 | KHERSON | Херсонська | 46.6354 | 32.6169 |
| 12 | POLTAVA | Полтавська | 49.5883 | 34.5514 |

---

#### EventTypes

Довідник типів подій. `EventTypeId` у `ConnectionEvents` посилається сюди.

```sql
CREATE TABLE EventTypes (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT NOT NULL UNIQUE,
    Description TEXT NOT NULL,
    Category    TEXT,
    Severity    TEXT,
    IsActive    INTEGER DEFAULT 1
);
```

**Початкові дані (seeded):**

| Id | Name | Category | Severity | Опис |
|----|------|----------|----------|------|
| 1 | ConnectionAttempt | Connection | Info | Спроба підключення |
| 2 | Connected | Connection | Info | Успішне з'єднання |
| 3 | Disconnected | Connection | Info | Розрив з'єднання |
| 4 | HelloReceived | Handshake | Info | Отримано HELLO |
| 5 | HelloSent | Handshake | Info | Відправлено HELLO |
| 6 | ChallengeReceived | Handshake | Info | Отримано CHALLENGE |
| 7 | ChallengeSent | Handshake | Info | Відправлено CHALLENGE |
| 8 | ResponseReceived | Handshake | Info | Отримано RESPONSE |
| 9 | ResponseSent | Handshake | Info | Відправлено RESPONSE |
| 10 | HandshakeReceived | Handshake | Info | Отримано підтвердження handshake |
| 11 | HandshakeSent | Handshake | Info | Відправлено підтвердження handshake |
| 12 | AccessDenied | Security | Warning | Відмова в доступі |
| 13 | AuthenticationFailed | Security | Warning | Невдала автентифікація |
| 14 | AuthenticationSuccess | Security | Info | Успішна автентифікація / передача файлу |
| 15 | InvalidFingerprint | Security | Error | Невірний Hardware Fingerprint |
| 16 | InvalidSignature | Security | Error | Невірний підпис |
| 17 | ConnectionError | Error | Error | Помилка з'єднання |
| 18 | SuspiciousActivity | Security | Warning | Підозріла активність |
| 19 | DataTransferred | Data | Info | Передача даних |
| 20 | PacketDropped | Data | Warning | Пакет відкинуто |

> **Важливо:** `EventTypeId` (Id в цій таблиці) і `EventType` (int значення enum `ConnectionEventType`) — різні поля в `ConnectionEvents`. Enum індексується від 0, тоді як Id в EventTypes — від 1 і залежить від порядку вставки.

---

#### ConnectionStatuses

Статуси з'єднань — використовуються в `PeerNodes.StatusId` і `Servers.StatusId`.

```sql
CREATE TABLE ConnectionStatuses (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT NOT NULL UNIQUE,
    Description TEXT NOT NULL,
    Color       TEXT,
    Icon        TEXT,
    IsFinal     INTEGER DEFAULT 0,   -- 1 = кінцевий стан (з'єднано/відключено/помилка)
    IsActive    INTEGER DEFAULT 1
);
```

**Початкові дані (seeded):**

| Id | Name | Color | Icon | IsFinal | Опис |
|----|------|-------|------|---------|------|
| 1 | Connected | #4EC9B0 | ✅ | 1 | Підключено |
| 2 | Disconnected | #808080 | ⭕ | 1 | Відключено |
| 3 | Connecting | #569CD6 | 🔄 | 0 | Підключення... |
| 4 | Authenticating | #DCDCAA | 🔐 | 0 | Автентифікація... |
| 5 | Failed | #F44747 | ❌ | 1 | Помилка |
| 6 | Rejected | #CE9178 | 🚫 | 1 | Відхилено |
| 7 | Timeout | #D16969 | ⏱️ | 1 | Тайм-аут |
| 8 | Unknown | #6A6A6A | ❓ | 0 | Невідомо |

---

### Основні таблиці

#### LocalBlackID

Власний Black-ID цього вузла. Зазвичай один активний запис (`IsActive = 1`).

```sql
CREATE TABLE LocalBlackID (
    Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    FullID               TEXT NOT NULL UNIQUE,        -- MAIN-KYIV-SERVER-2N4D
    RoleId               INTEGER NOT NULL,            -- FK -> Roles
    CityId               INTEGER NOT NULL,            -- FK -> Cities
    Role                 TEXT NOT NULL,               -- "MAIN"
    City                 TEXT NOT NULL,               -- "KYIV"
    Name                 TEXT NOT NULL,               -- "SERVER"
    Code                 TEXT NOT NULL,               -- "2N4D"
    HardwareFingerprint  TEXT NOT NULL,               -- hex-рядок з характеристик заліза
    Signature            TEXT NOT NULL,               -- підпис для автентифікації
    CreatedAt            TEXT NOT NULL,               -- ISO 8601
    SignatureCreatedAt   TEXT NOT NULL,
    IsActive             INTEGER DEFAULT 1,
    FOREIGN KEY (RoleId) REFERENCES Roles(Id),
    FOREIGN KEY (CityId) REFERENCES Cities(Id)
);
```

**Індекси:**
```sql
idx_localblackid_role    ON LocalBlackID(RoleId)
idx_localblackid_city    ON LocalBlackID(CityId)
idx_localblackid_active  ON LocalBlackID(IsActive)
```

---

#### PeerNodes

Телефонна книга — всі відомі піри. Серце системи ідентифікації.

```sql
CREATE TABLE PeerNodes (
    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    BlackID               TEXT NOT NULL UNIQUE,       -- MAIN-KYIV-SERVER-2N4D або peer_IP_Port (тимчасовий)
    Address               TEXT NOT NULL,              -- IP-адреса
    Port                  INTEGER NOT NULL DEFAULT 9999,
    DisplayName           TEXT NOT NULL,
    Description           TEXT,
    IsTrusted             INTEGER DEFAULT 0,          -- 0/1
    LastConnectedAt       TEXT,                       -- ISO 8601, NULL якщо ще не підключались
    CreatedAt             TEXT NOT NULL,
    IsActive              INTEGER DEFAULT 1,
    StatusId              INTEGER,                    -- FK -> ConnectionStatuses (NULL якщо не відомо)
    SuccessfulConnections INTEGER DEFAULT 0,
    FailedConnections     INTEGER DEFAULT 0,
    PublicKey             TEXT,                       -- для майбутньої асиметричної автентифікації
    Tags                  TEXT,                       -- довільні мітки через кому
    FOREIGN KEY (StatusId) REFERENCES ConnectionStatuses(Id)
);
```

**Індекси:**
```sql
idx_peernodes_blackid  ON PeerNodes(BlackID)   -- пошук за ID (основний)
idx_peernodes_active   ON PeerNodes(IsActive)
idx_peernodes_status   ON PeerNodes(StatusId)
```

**Особливості роботи:**

- `BlackID UNIQUE` — SQLite не дозволяє два записи з однаковим BlackID
- При першому підключенні (до авто-обміну) BlackID = `peer_{IP}_{Port}` (тимчасовий)
- Після `HandleNodeInfoPacket` BlackID оновлюється до реального (або temp-запис видаляється, якщо реальний вже є)
- `UpdatePeerNode` оновлює за `Id` — дозволяє змінити поле `BlackID` без конфлікту UNIQUE (якщо рядки мають різні Id)
- Пошук при підключенні ведеться **за IP** (`Address`), а не за портом — порт змінюється щоразу

---

#### ConnectionEvents

Журнал аудиту всіх подій. Пишеться при підключенні, відключенні та передачі файлів.

```sql
CREATE TABLE ConnectionEvents (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    RemoteBlackID    TEXT,                           -- NULL якщо BlackID невідомий (до node info обміну)
    RemoteIP         TEXT NOT NULL,
    RemotePort       INTEGER NOT NULL,
    InitiatorBlackID TEXT,                           -- наш BlackID (хто ініціював)
    TargetBlackID    TEXT,                           -- цільовий BlackID
    EventTypeId      INTEGER NOT NULL,               -- FK -> EventTypes (Id)
    EventType        INTEGER NOT NULL,               -- (int)ConnectionEventType enum
    Direction        INTEGER NOT NULL,               -- 0=Inbound, 1=Outbound
    Message          TEXT NOT NULL,
    ErrorDetails     TEXT,                           -- деталі помилки (якщо є)
    IsAuthenticated  INTEGER DEFAULT 0,              -- 0/1
    Timestamp        TEXT NOT NULL,                  -- ISO 8601 UTC
    DurationSeconds  REAL,                           -- тривалість з'єднання (NULL для файлів)
    BytesSent        INTEGER DEFAULT 0,
    BytesReceived    INTEGER DEFAULT 0,
    FOREIGN KEY (EventTypeId) REFERENCES EventTypes(Id)
);
```

**Індекси:**
```sql
idx_events_timestamp       ON ConnectionEvents(Timestamp)
idx_events_remote_blackid  ON ConnectionEvents(RemoteBlackID)
idx_events_initiator       ON ConnectionEvents(InitiatorBlackID)
idx_events_target          ON ConnectionEvents(TargetBlackID)
idx_events_type            ON ConnectionEvents(EventTypeId)
```

**Що і коли пишеться:**

| Подія | EventTypeId | EventType (enum) | Direction | BytesSent | BytesReceived |
|-------|-------------|-----------------|-----------|-----------|---------------|
| P2P підключено | 2 (Connected) | 1 | 1 (Outbound) | 0 | 0 |
| З'єднання розірвано | 3 (Disconnected) | 2 | 1 (Outbound) | 0 | 0 |
| Файл надіслано | 14 (AuthenticationSuccess) | 5 | 1 (Outbound) | розмір файлу | 0 |
| Файл отримано | 14 (AuthenticationSuccess) | 5 | 0 (Inbound) | 0 | розмір файлу |

> `EventTypeId` шукається в таблиці `EventTypes` за назвою enum (`ev.EventType.ToString()`). Якщо назва не знайдена — підставляється `(int)ev.EventType` як fallback. SQLite не enforces FK за замовчуванням (`PRAGMA foreign_keys = OFF`), тому запис зберігається навіть при невідомому EventTypeId.

**Зв'язок між enum та EventTypes:**

```
ConnectionEventType enum          EventTypes таблиця
(int значення)                    (Id = порядок вставки)

0 = ConnectionAttempt      →  Id=1   ConnectionAttempt
1 = Connected              →  Id=2   Connected
2 = Disconnected           →  Id=3   Disconnected
3 = AccessDenied           →  Id=12  AccessDenied
4 = AuthenticationFailed   →  Id=13  AuthenticationFailed
5 = AuthenticationSuccess  →  Id=14  AuthenticationSuccess
6 = ConnectionError        →  Id=17  ConnectionError
7 = SuspiciousActivity     →  Id=18  SuspiciousActivity
```

---

### Модуль мапи серверів

#### Servers

Реєстр відомих серверів мережі (зарезервовано для майбутнього модуля).

```sql
CREATE TABLE Servers (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    BlackID             TEXT NOT NULL UNIQUE,
    HardwareFingerprint TEXT NOT NULL,
    StatusId            INTEGER NOT NULL,             -- FK -> ConnectionStatuses
    DisplayName         TEXT NOT NULL,
    Description         TEXT,
    OperatingSystem     TEXT,
    FirewallVersion     TEXT,
    LastSeenAt          TEXT,
    CreatedAt           TEXT NOT NULL,
    IsActive            INTEGER DEFAULT 1,
    Metadata            TEXT,                         -- JSON з довільними даними
    FOREIGN KEY (StatusId) REFERENCES ConnectionStatuses(Id)
);
```

**Індекси:**
```sql
idx_servers_blackid  ON Servers(BlackID)
idx_servers_status   ON Servers(StatusId)
idx_servers_active   ON Servers(IsActive)
```

#### ServerLocations

Геолокація серверів (1:1 до `Servers`).

```sql
CREATE TABLE ServerLocations (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    ServerId       INTEGER NOT NULL UNIQUE,           -- FK -> Servers (CASCADE DELETE)
    Latitude       REAL NOT NULL,
    Longitude      REAL NOT NULL,
    IPAddress      TEXT NOT NULL,
    Port           INTEGER DEFAULT 9999,
    Address        TEXT,                              -- текстова адреса
    CityId         INTEGER,                           -- FK -> Cities
    CountryCode    TEXT,
    Region         TEXT,
    PostalCode     TEXT,
    AccuracyMeters REAL,
    UpdatedAt      TEXT NOT NULL,
    CreatedAt      TEXT NOT NULL,
    FOREIGN KEY (ServerId) REFERENCES Servers(Id) ON DELETE CASCADE,
    FOREIGN KEY (CityId) REFERENCES Cities(Id)
);
```

**Індекси:**
```sql
idx_serverlocations_server  ON ServerLocations(ServerId)
idx_serverlocations_city    ON ServerLocations(CityId)
idx_serverlocations_coords  ON ServerLocations(Latitude, Longitude)
```

---

### DatabaseVersion

Версіонування схеми. Поточна версія: **2**.

```sql
CREATE TABLE DatabaseVersion (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Version     INTEGER NOT NULL,
    AppliedAt   TEXT NOT NULL,
    Description TEXT
);
```

При запуску `BlackCatDatabase` перевіряє версію:
1. Якщо БД не існує → `CreateFreshDatabase()` + `DataSeeder.SeedAll()` + запис версії 2
2. Якщо БД існує → `DatabaseMigrator.RunMigrations()` (застосовує дельти)
3. Якщо міграція кидає виняток → `DropDatabase()` + крок 1

---

### Зведена таблиця всіх таблиць

| Таблиця | Рядків (типово) | Призначення |
|---------|-----------------|-------------|
| Roles | 6 | Довідник ролей для Black-ID |
| Cities | 12 | Довідник міст для Black-ID |
| EventTypes | 20 | Довідник типів подій |
| ConnectionStatuses | 8 | Довідник статусів з'єднань |
| LocalBlackID | 1 | Наш власний Black-ID |
| PeerNodes | N | Телефонна книга пірів |
| ConnectionEvents | N | Журнал аудиту |
| Servers | N | Реєстр серверів (резерв) |
| ServerLocations | N | Геолокація серверів (резерв) |
| DatabaseVersion | 1 | Версія схеми |

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
