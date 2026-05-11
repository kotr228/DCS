namespace BlackCat.Shared.Models;

/// <summary>
/// Базовий клас для повідомлень handshake
/// </summary>
public abstract class HandshakeMessage
{
    /// <summary>
    /// Тип повідомлення
    /// </summary>
    public HandshakeMessageType Type { get; set; }

    /// <summary>
    /// Timestamp повідомлення
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Тип повідомлення handshake
/// </summary>
public enum HandshakeMessageType
{
    /// <summary>
    /// Hello - ініціація з'єднання
    /// </summary>
    Hello = 1,

    /// <summary>
    /// Challenge - виклик для автентифікації
    /// </summary>
    Challenge = 2,

    /// <summary>
    /// Response - відповідь на challenge
    /// </summary>
    Response = 3,

    /// <summary>
    /// Accept - підтвердження успішної автентифікації
    /// </summary>
    Accept = 4,

    /// <summary>
    /// Reject - відмова в автентифікації
    /// </summary>
    Reject = 5
}

/// <summary>
/// Hello повідомлення - ініціація з'єднання
/// </summary>
public class HelloMessage : HandshakeMessage
{
    public HelloMessage()
    {
        Type = HandshakeMessageType.Hello;
    }

    /// <summary>
    /// Black-ID відправника
    /// </summary>
    public string BlackID { get; set; } = string.Empty;

    /// <summary>
    /// Версія протоколу
    /// </summary>
    public string ProtocolVersion { get; set; } = "1.0";

    /// <summary>
    /// Публічний ключ відправника (опціонально)
    /// </summary>
    public string? PublicKey { get; set; }
}

/// <summary>
/// Challenge повідомлення - виклик для верифікації
/// </summary>
public class ChallengeMessage : HandshakeMessage
{
    public ChallengeMessage()
    {
        Type = HandshakeMessageType.Challenge;
    }

    /// <summary>
    /// Black-ID відправника
    /// </summary>
    public string BlackID { get; set; } = string.Empty;

    /// <summary>
    /// Випадковий challenge
    /// </summary>
    public string Challenge { get; set; } = string.Empty;

    /// <summary>
    /// Nonce для захисту від replay attack
    /// </summary>
    public string Nonce { get; set; } = string.Empty;
}

/// <summary>
/// Response повідомлення - відповідь на challenge
/// </summary>
public class ResponseMessage : HandshakeMessage
{
    public ResponseMessage()
    {
        Type = HandshakeMessageType.Response;
    }

    /// <summary>
    /// Black-ID відправника
    /// </summary>
    public string BlackID { get; set; } = string.Empty;

    /// <summary>
    /// Підписаний challenge
    /// </summary>
    public string SignedChallenge { get; set; } = string.Empty;

    /// <summary>
    /// Hardware fingerprint для додаткової верифікації
    /// </summary>
    public string HardwareFingerprint { get; set; } = string.Empty;
}

/// <summary>
/// Accept повідомлення - підтвердження успішної автентифікації
/// </summary>
public class AcceptMessage : HandshakeMessage
{
    public AcceptMessage()
    {
        Type = HandshakeMessageType.Accept;
    }

    /// <summary>
    /// Black-ID відправника
    /// </summary>
    public string BlackID { get; set; } = string.Empty;

    /// <summary>
    /// Повідомлення про прийняття
    /// </summary>
    public string Message { get; set; } = "Authenticated successfully";

    /// <summary>
    /// Session ID для подальшої комунікації
    /// </summary>
    public string SessionId { get; set; } = string.Empty;
}

/// <summary>
/// Reject повідомлення - відмова в автентифікації
/// </summary>
public class RejectMessage : HandshakeMessage
{
    public RejectMessage()
    {
        Type = HandshakeMessageType.Reject;
    }

    /// <summary>
    /// Причина відмови
    /// </summary>
    public string Reason { get; set; } = "Authentication failed";

    /// <summary>
    /// Додаткові деталі (опціонально)
    /// </summary>
    public string? Details { get; set; }
}
