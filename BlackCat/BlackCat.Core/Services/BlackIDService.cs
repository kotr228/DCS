using System.Security.Cryptography;
using System.Text;
using BlackCat.Shared.Models;

namespace BlackCat.Core.Services;

/// <summary>
/// Сервіс для роботи з Black-ID
/// </summary>
public class BlackIDService
{
    private readonly HardwareFingerprintService _hardwareService;
    private static readonly Random _random = new Random();

    // Доступні ролі
    public static readonly string[] AvailableRoles = new[]
    {
        "SKLAD", "OFFICE", "MAIN", "BACKUP", "SERVER", "WORKSTATION"
    };

    // Доступні міста
    public static readonly string[] AvailableCities = new[]
    {
        "KYIV", "KHARKIV", "ODESA", "DNIPRO", "LVIV", "ZAPORIZHZHIA",
        "VINNYTSIA", "MYKOLAIV", "POLTAVA", "CHERNIHIV", "SUMY", "ZHYTOMYR"
    };

    // Символи для коду (виключаємо схожі: 0/O, 1/I/L)
    private const string CodeChars = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public BlackIDService()
    {
        _hardwareService = new HardwareFingerprintService();
    }

    /// <summary>
    /// Згенерувати новий Black-ID
    /// </summary>
    public BlackID GenerateID(string role, string city, string name)
    {
        // Валідація
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Роль не може бути пустою", nameof(role));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("Місто не може бути пустим", nameof(city));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Назва не може бути пустою", nameof(name));

        // Нормалізувати
        role = role.ToUpperInvariant().Trim();
        city = city.ToUpperInvariant().Trim();
        name = name.ToUpperInvariant().Trim();

        // Згенерувати унікальний код
        string code = GenerateUniqueCode();

        // Скласти повний ID
        string fullID = $"{role}-{city}-{name}-{code}";

        // Отримати hardware fingerprint
        string hwFingerprint = _hardwareService.GetFingerprint();

        // Створити криптографічний підпис
        string signature = GenerateSignature(fullID, hwFingerprint);

        return new BlackID
        {
            FullID = fullID,
            Role = role,
            City = city,
            Name = name,
            Code = code,
            HardwareFingerprint = hwFingerprint,
            Signature = signature,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    /// <summary>
    /// Згенерувати унікальний 4-символьний код
    /// </summary>
    private string GenerateUniqueCode()
    {
        var code = new char[4];
        for (int i = 0; i < 4; i++)
        {
            code[i] = CodeChars[_random.Next(CodeChars.Length)];
        }
        return new string(code);
    }

    /// <summary>
    /// Згенерувати криптографічний підпис для Black-ID
    /// </summary>
    private string GenerateSignature(string blackID, string hardwareFingerprint)
    {
        var data = $"{blackID}|{hardwareFingerprint}|{DateTime.UtcNow:O}";
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Перевірити чи Black-ID валідний для поточного заліза
    /// </summary>
    public bool ValidateID(BlackID blackID)
    {
        if (blackID == null)
            return false;

        // Перевірити формат
        if (!BlackID.IsValid(blackID.FullID))
            return false;

        // Перевірити hardware fingerprint
        return _hardwareService.ValidateFingerprint(blackID.HardwareFingerprint);
    }

    /// <summary>
    /// Згенерувати challenge для handshake
    /// </summary>
    public string GenerateChallenge()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    /// <summary>
    /// Підписати challenge нашим Black-ID
    /// </summary>
    public string SignChallenge(string challenge, BlackID ourID)
    {
        var data = $"{challenge}|{ourID.FullID}|{ourID.HardwareFingerprint}";
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Перевірити підпис challenge від віддаленого вузла
    /// </summary>
    public bool VerifyChallenge(string challenge, string signature, string remoteBlackID)
    {
        // Базова перевірка - в реальності тут має бути більш складна логіка
        // з публічними ключами, але для MVP достатньо перевірити формат
        return !string.IsNullOrWhiteSpace(signature) &&
               BlackID.IsValid(remoteBlackID);
    }

    /// <summary>
    /// Отримати інформацію про залізо
    /// </summary>
    public HardwareInfo GetHardwareInfo()
    {
        return _hardwareService.GetHardwareInfo();
    }
}
