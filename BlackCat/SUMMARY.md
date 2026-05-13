# BlackCat — Короткий довідник

## Що це

BlackCat — брандмауер з вбудованим зашифрованим P2P-тунелем. Два вузли з'єднуються напряму через NAT (UDP hole punching) і обмінюються трафіком, зашифрованим власним алгоритмом MQE (Modular Quaternion Encryption).

---

## Ключові компоненти

| Компонент | Файл | Що робить |
|-----------|------|-----------|
| MQE шифрування | `Crypto/MQECryptoService.cs` | Encrypt/Decrypt через кватерніони |
| Кватерніонна математика | `Crypto/Quaternion.cs` | Множення Гамільтона, mod inverse |
| UDP тунель | `NetworkCore/UdpEncryptedTunnel.cs` | Зашифрований UDP + keepalive |
| Hole punching | `Core/Services/TunnelManager.cs` | ManualHolePunchAsync |
| STUN | `NetworkCore/StunClient.cs` | Визначення публічного endpoint |
| Фільтр пакетів | `Core/FilterEngine.cs` | Allow/Block/Tunnel за правилами |
| Телефонна книга | `Core/Data/PeerNodeRepository.cs` | CRUD для SQLite PeerNodes |
| Головне вікно | `UI/MainWindow.xaml.cs` | P2P signaling, файли, статистика |

---

## Маркери типів пакетів

```
0xFF          — keepalive (raw, не через MQE)
0xBC 0xAA     — hole punch сигнал
0xBC 0x1D     — node info exchange (JSON з BlackID)
0xBC 0x1E     — file transfer
```

---

## Формати

### Black-ID
```
РОЛЬ-МІСТО-НАЗВА-КОД
MAIN-KYIV-SERVER-2N4D
```

### P2P код (endpoint)
```
IP:порт
81.162.255.205:54321
```

### Node info пакет
```
[0xBC][0x1D] + UTF-8 JSON
{"blackID":"MAIN-KYIV-SERVER-2N4D","displayName":"MAIN-KYIV-SERVER-2N4D"}
```

### File transfer пакет
```
[0xBC][0x1E][4B: довжина імені LE][ім'я UTF-8][вміст]
```

---

## Тимчасові папки

```
temp_data/    — системні файли програми
  nodeinfo_sent_{ourID}.json
  nodeinfo_recv_{IP}.json
  sent_{peerID}_{filename}
  recv_{IP}_{time}_{name}.meta

temp_files/   — отримані від пірів файли
  hello_{BlackID}_{час}.txt     (автоматично при підключенні)
```

---

## Алгоритм MQE

```
Ключ:     K = SHA256(masterSecret + timestamp)[0..3] -> Quaternion(W,X,Y,Z)
Умова:    norm(K) % 2 != 0  (непарна норма -> оборотний)
Encrypt:  C = P * K   (Hamilton *, mod 256, блоки по 4 байти)
Decrypt:  P = C * K⁻¹
Захист:   timestamp ±30 сек (replay), SHA256 checksum (integrity)
```

---

## База даних

SQLite файл `blackcat.db` поряд з `.exe`.

```sql
PeerNodes       -- телефонна книга (BlackID UNIQUE)
LocalBlackID    -- наш власний Black-ID
FilterRules     -- правила брандмауера
ConnectionEvents -- журнал: підключення, файли (BytesSent/BytesReceived)
EventTypes      -- довідник типів подій
DatabaseVersion -- версія схеми
```

**EventType значення в ConnectionEvents:**

| Значення | Enum | Коли пишеться |
|----------|------|---------------|
| 1 | Connected | P2P з'єднання встановлено |
| 2 | Disconnected | З'єднання розірвано |
| 5 | AuthenticationSuccess | Файл надіслано або отримано |

---

## Порти

| Порт | Протокол | Призначення |
|------|----------|-------------|
| 9999 | TCP/UDP | Тунель (вхідні + P2P) |
| 19302 | UDP | STUN (stun.l.google.com) |

---

## Залежності NuGet

```
Microsoft.Data.Sqlite
LiveCharts.Wpf
Serilog + Serilog.Sinks.File + Serilog.Sinks.Console
```
