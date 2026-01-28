using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlackCat.Shared.Models;
using BlackCat.Core.Data;

namespace BlackCat.Core.Services;

/// <summary>
/// Сервіс для виконання handshake між вузлами
/// </summary>
public class HandshakeService
{
    private readonly BlackIDService _blackIDService;
    private readonly PeerNodeRepository _peerNodeRepository;
    private readonly ConnectionEventRepository _eventRepository;
    private readonly Dictionary<string, PendingHandshake> _pendingHandshakes = new();
    private readonly object _lockObject = new();

    public HandshakeService(
        BlackIDService blackIDService,
        PeerNodeRepository peerNodeRepository,
        ConnectionEventRepository eventRepository)
    {
        _blackIDService = blackIDService;
        _peerNodeRepository = peerNodeRepository;
        _eventRepository = eventRepository;
    }

    /// <summary>
    /// Розпочати handshake як ініціатор (клієнт)
    /// </summary>
    public HelloMessage InitiateHandshake(BlackID ourID)
    {
        return new HelloMessage
        {
            BlackID = ourID.FullID,
            ProtocolVersion = "1.0",
            PublicKey = null, // TODO: Додати підтримку публічних ключів
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Обробити Hello повідомлення та відправити Challenge (сервер)
    /// </summary>
    public ChallengeMessage HandleHello(HelloMessage hello, BlackID ourID, string remoteIP)
    {
        // Перевірити формат Black-ID
        if (!BlackID.IsValid(hello.BlackID))
        {
            throw new InvalidOperationException($"Невірний формат Black-ID: {hello.BlackID}");
        }

        // Логувати подію
        _eventRepository.LogEvent(new ConnectionEvent
        {
            RemoteBlackID = hello.BlackID,
            RemoteIP = remoteIP,
            RemotePort = 0,
            EventType = ConnectionEventType.ConnectionAttempt,
            Direction = ConnectionDirection.Inbound,
            Message = $"Отримано Hello від {hello.BlackID}",
            Timestamp = DateTime.UtcNow
        });

        // Згенерувати challenge
        var challenge = _blackIDService.GenerateChallenge();
        var nonce = GenerateNonce();

        // Зберегти pending handshake
        lock (_lockObject)
        {
            _pendingHandshakes[hello.BlackID] = new PendingHandshake
            {
                RemoteBlackID = hello.BlackID,
                Challenge = challenge,
                Nonce = nonce,
                Timestamp = DateTime.UtcNow
            };
        }

        return new ChallengeMessage
        {
            BlackID = ourID.FullID,
            Challenge = challenge,
            Nonce = nonce,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Обробити Challenge та створити Response (клієнт)
    /// </summary>
    public ResponseMessage HandleChallenge(ChallengeMessage challenge, BlackID ourID)
    {
        // Підписати challenge нашим Black-ID
        var signature = _blackIDService.SignChallenge(challenge.Challenge, ourID);

        return new ResponseMessage
        {
            BlackID = ourID.FullID,
            SignedChallenge = signature,
            HardwareFingerprint = ourID.HardwareFingerprint,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Обробити Response та перевірити автентифікацію (сервер)
    /// </summary>
    public HandshakeMessage HandleResponse(ResponseMessage response, string remoteIP)
    {
        // Знайти pending handshake
        PendingHandshake? pending;
        lock (_lockObject)
        {
            if (!_pendingHandshakes.TryGetValue(response.BlackID, out pending))
            {
                return new RejectMessage
                {
                    Reason = "Невідомий Black-ID або timeout",
                    Details = "Handshake не було ініційовано або вже завершився"
                };
            }

            // Видалити з pending
            _pendingHandshakes.Remove(response.BlackID);
        }

        // Перевірити timeout (5 хвилин)
        if ((DateTime.UtcNow - pending.Timestamp).TotalMinutes > 5)
        {
            _eventRepository.LogEvent(new ConnectionEvent
            {
                RemoteBlackID = response.BlackID,
                RemoteIP = remoteIP,
                RemotePort = 0,
                EventType = ConnectionEventType.AuthenticationFailed,
                Direction = ConnectionDirection.Inbound,
                Message = "Handshake timeout",
                Timestamp = DateTime.UtcNow
            });

            return new RejectMessage
            {
                Reason = "Handshake timeout",
                Details = "Відповідь надійшла занадто пізно"
            };
        }

        // Перевірити підпис
        bool isValid = _blackIDService.VerifyChallenge(
            pending.Challenge,
            response.SignedChallenge,
            response.BlackID
        );

        if (!isValid)
        {
            _eventRepository.LogEvent(new ConnectionEvent
            {
                RemoteBlackID = response.BlackID,
                RemoteIP = remoteIP,
                RemotePort = 0,
                EventType = ConnectionEventType.AuthenticationFailed,
                Direction = ConnectionDirection.Inbound,
                Message = "Невірний підпис challenge",
                Timestamp = DateTime.UtcNow
            });

            return new RejectMessage
            {
                Reason = "Authentication failed",
                Details = "Невірний підпис challenge"
            };
        }

        // Перевірити чи є цей вузол в телефонній книзі
        var peerNode = _peerNodeRepository.GetPeerNodeByBlackID(response.BlackID);
        if (peerNode == null)
        {
            // Вузол не знайдено - це новий вузол
            _eventRepository.LogEvent(new ConnectionEvent
            {
                RemoteBlackID = response.BlackID,
                RemoteIP = remoteIP,
                RemotePort = 0,
                EventType = ConnectionEventType.AuthenticationSuccess,
                Direction = ConnectionDirection.Inbound,
                Message = $"Автентифікація успішна (новий вузол): {response.BlackID}",
                IsAuthenticated = true,
                Timestamp = DateTime.UtcNow
            });

            // Можна додати автоматично або вимагати ручного підтвердження
            // Поки що не довіряємо автоматично
            return new AcceptMessage
            {
                BlackID = response.BlackID,
                Message = "Authenticated (new node, not trusted yet)",
                SessionId = GenerateSessionId()
            };
        }

        // Оновити статистику вузла
        _peerNodeRepository.RecordSuccessfulConnection(peerNode.Id);

        _eventRepository.LogEvent(new ConnectionEvent
        {
            RemoteBlackID = response.BlackID,
            RemoteIP = remoteIP,
            RemotePort = 0,
            EventType = ConnectionEventType.AuthenticationSuccess,
            Direction = ConnectionDirection.Inbound,
            Message = $"Автентифікація успішна: {response.BlackID}",
            IsAuthenticated = true,
            Timestamp = DateTime.UtcNow
        });

        return new AcceptMessage
        {
            BlackID = response.BlackID,
            Message = peerNode.IsTrusted ? "Authenticated (trusted node)" : "Authenticated (untrusted node)",
            SessionId = GenerateSessionId()
        };
    }

    /// <summary>
    /// Серіалізувати повідомлення в JSON
    /// </summary>
    public byte[] SerializeMessage(HandshakeMessage message)
    {
        var json = JsonSerializer.Serialize(message, message.GetType());
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Десеріалізувати повідомлення з JSON
    /// </summary>
    public HandshakeMessage? DeserializeMessage(byte[] data, HandshakeMessageType type)
    {
        var json = Encoding.UTF8.GetString(data);

        return type switch
        {
            HandshakeMessageType.Hello => JsonSerializer.Deserialize<HelloMessage>(json),
            HandshakeMessageType.Challenge => JsonSerializer.Deserialize<ChallengeMessage>(json),
            HandshakeMessageType.Response => JsonSerializer.Deserialize<ResponseMessage>(json),
            HandshakeMessageType.Accept => JsonSerializer.Deserialize<AcceptMessage>(json),
            HandshakeMessageType.Reject => JsonSerializer.Deserialize<RejectMessage>(json),
            _ => null
        };
    }

    /// <summary>
    /// Очистити застарілі pending handshakes
    /// </summary>
    public void CleanupExpiredHandshakes()
    {
        lock (_lockObject)
        {
            var expired = _pendingHandshakes
                .Where(kvp => (DateTime.UtcNow - kvp.Value.Timestamp).TotalMinutes > 5)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expired)
            {
                _pendingHandshakes.Remove(key);
            }
        }
    }

    /// <summary>
    /// Згенерувати nonce
    /// </summary>
    private string GenerateNonce()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Згенерувати session ID
    /// </summary>
    private string GenerateSessionId()
    {
        return Guid.NewGuid().ToString("N");
    }
}

/// <summary>
/// Pending handshake
/// </summary>
internal class PendingHandshake
{
    public string RemoteBlackID { get; set; } = string.Empty;
    public string Challenge { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
