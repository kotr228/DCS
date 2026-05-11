using System.Security.Cryptography;
using System.Text;
using BlackCat.Shared.Models;

namespace BlackCat.Crypto;

/// <summary>
/// Modular Quaternion Encryption (MQE) Service
/// Криптографічна служба на основі кватерніонів
/// </summary>
public class MQECryptoService
{
    private readonly string _masterSecret;
    private const int BLOCK_SIZE = 4; // 4 байти = 1 кватерніон
    private const int MODULUS = 256;
    // 30с: достатньо для WAN-латентності + розсинхронізації годинників між машинами,
    // і при цьому все ще захищає від replay-атак.
    private const int TIMESTAMP_VALIDITY_SECONDS = 30;

    public MQECryptoService(string masterSecret)
    {
        if (string.IsNullOrWhiteSpace(masterSecret))
            throw new ArgumentException("Master secret не може бути порожнім", nameof(masterSecret));

        _masterSecret = masterSecret;
    }

    /// <summary>
    /// Шифрування даних (відправка пакету)
    /// </summary>
    public TunnelPacket Encrypt(byte[] data, string sourceIP, string destinationIP)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Дані не можуть бути порожніми", nameof(data));

        // Крок 1: Отримати поточний час
        long timestamp = DateTime.UtcNow.Ticks;

        // Крок 2: Згенерувати сесійний ключ
        Quaternion keyQuaternion = GenerateSessionKey(timestamp);

        // Крок 3: Шифрування блоками
        byte[] encryptedData = EncryptBlocks(data, keyQuaternion);

        // Крок 4: Обчислити контрольну суму
        byte[] checksum = ComputeChecksum(encryptedData);

        // Крок 5: Сформувати пакет
        return new TunnelPacket
        {
            Version = 1,
            Timestamp = timestamp,
            EncryptedPayload = encryptedData,
            Checksum = checksum,
            SourceIP = sourceIP,
            DestinationIP = destinationIP
        };
    }

    /// <summary>
    /// Розшифрування даних (отримання пакету)
    /// </summary>
    public byte[] Decrypt(TunnelPacket packet)
    {
        if (packet == null)
            throw new ArgumentNullException(nameof(packet));

        // Крок 2: Валідація часу (захист від Replay Attack)
        long currentTime = DateTime.UtcNow.Ticks;
        long timeDifference = Math.Abs(currentTime - packet.Timestamp);
        long maxDifference = TimeSpan.FromSeconds(TIMESTAMP_VALIDITY_SECONDS).Ticks;

        if (timeDifference > maxDifference)
        {
            throw new CryptographicException(
                $"Пакет застарілий або атака повтору. Різниця: {TimeSpan.FromTicks(timeDifference).TotalSeconds:F2}с");
        }

        // Крок 3: Валідація контрольної суми
        byte[] expectedChecksum = ComputeChecksum(packet.EncryptedPayload);
        if (!ChecksumMatches(packet.Checksum, expectedChecksum))
        {
            throw new CryptographicException("Контрольна сума не співпадає. Пакет пошкоджено або підроблено.");
        }

        // Крок 4: Відновлення ключа
        Quaternion keyQuaternion = GenerateSessionKey(packet.Timestamp);

        // Крок 5: Обчислення оберненого ключа
        Quaternion inverseKey = keyQuaternion.ModularInverse(MODULUS);

        // Крок 6: Розшифрування блоками
        byte[] decryptedData = DecryptBlocks(packet.EncryptedPayload, inverseKey);

        return decryptedData;
    }

    /// <summary>
    /// Генерація сесійного ключа на основі секрету та часу
    /// </summary>
    private Quaternion GenerateSessionKey(long timestamp)
    {
        // Змішати секрет і час
        string rawKey = _masterSecret + timestamp.ToString();

        // Хешувати SHA256
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));

        // Взяти перші 4 байти для компонентів кватерніона
        int w = hash[0];
        int x = hash[1];
        int y = hash[2];
        int z = hash[3];

        // Переконатися, що норма непарна (для оборотності)
        var key = new Quaternion(w, x, y, z);
        long norm = key.Norm();

        if (norm % 2 == 0)
        {
            w += 1; // Зробити норму непарною
        }

        return new Quaternion(w, x, y, z);
    }

    /// <summary>
    /// Шифрування даних блоками по 4 байти
    /// </summary>
    private byte[] EncryptBlocks(byte[] data, Quaternion key)
    {
        // Додати padding, щоб розмір був кратний 4
        byte[] paddedData = AddPadding(data);
        List<byte> encrypted = new List<byte>();

        // Обробити кожен блок
        for (int i = 0; i < paddedData.Length; i += BLOCK_SIZE)
        {
            // Створити кватерніон з блоку даних
            Quaternion dataBlock = Quaternion.FromBytes(
                paddedData[i],
                paddedData[i + 1],
                paddedData[i + 2],
                paddedData[i + 3]
            );

            // Множення Гамільтона
            Quaternion cipherBlock = dataBlock * key;

            // Нормалізація та додавання до результату
            encrypted.AddRange(cipherBlock.ToBytes());
        }

        return encrypted.ToArray();
    }

    /// <summary>
    /// Розшифрування даних блоками
    /// </summary>
    private byte[] DecryptBlocks(byte[] encryptedData, Quaternion inverseKey)
    {
        List<byte> decrypted = new List<byte>();

        // Обробити кожен блок
        for (int i = 0; i < encryptedData.Length; i += BLOCK_SIZE)
        {
            // Створити кватерніон з зашифрованого блоку
            Quaternion cipherBlock = Quaternion.FromBytes(
                encryptedData[i],
                encryptedData[i + 1],
                encryptedData[i + 2],
                encryptedData[i + 3]
            );

            // Множення на обернений ключ
            Quaternion originalBlock = cipherBlock * inverseKey;

            // Нормалізація та додавання до результату
            decrypted.AddRange(originalBlock.ToBytes());
        }

        // Видалити padding
        return RemovePadding(decrypted.ToArray());
    }

    /// <summary>
    /// Додати PKCS7 padding
    /// </summary>
    private byte[] AddPadding(byte[] data)
    {
        int paddingSize = BLOCK_SIZE - (data.Length % BLOCK_SIZE);
        if (paddingSize == BLOCK_SIZE) paddingSize = 0;

        if (paddingSize == 0) return data;

        byte[] padded = new byte[data.Length + paddingSize];
        Array.Copy(data, padded, data.Length);

        // Заповнити padding байтами зі значенням розміру padding
        for (int i = data.Length; i < padded.Length; i++)
        {
            padded[i] = (byte)paddingSize;
        }

        return padded;
    }

    /// <summary>
    /// Видалити PKCS7 padding
    /// </summary>
    private byte[] RemovePadding(byte[] data)
    {
        if (data.Length == 0) return data;

        byte paddingSize = data[data.Length - 1];

        // Валідація padding
        if (paddingSize > BLOCK_SIZE || paddingSize > data.Length)
            return data;

        // Перевірити, чи всі padding байти мають правильне значення
        for (int i = data.Length - paddingSize; i < data.Length; i++)
        {
            if (data[i] != paddingSize)
                return data; // Невалідний padding
        }

        byte[] result = new byte[data.Length - paddingSize];
        Array.Copy(data, result, result.Length);
        return result;
    }

    /// <summary>
    /// Обчислення контрольної суми (SHA256)
    /// </summary>
    private byte[] ComputeChecksum(byte[] data)
    {
        using (var sha256 = SHA256.Create())
        {
            return sha256.ComputeHash(data);
        }
    }

    /// <summary>
    /// Перевірка контрольної суми
    /// </summary>
    private bool ChecksumMatches(byte[] checksum1, byte[] checksum2)
    {
        if (checksum1.Length != checksum2.Length)
            return false;

        for (int i = 0; i < checksum1.Length; i++)
        {
            if (checksum1[i] != checksum2[i])
                return false;
        }

        return true;
    }
}
