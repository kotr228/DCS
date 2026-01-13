using System.Security.Cryptography;
using System.Text;

namespace BlackCat.Crypto;

/// <summary>
/// Математичний кватерніон для шифрування (w + xi + yj + zk)
/// </summary>
public struct Quaternion
{
    public int W { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    public Quaternion(int w, int x, int y, int z)
    {
        W = w;
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>
    /// Створити кватерніон з 4 байтів
    /// </summary>
    public static Quaternion FromBytes(byte b1, byte b2, byte b3, byte b4)
    {
        return new Quaternion(b1, b2, b3, b4);
    }

    /// <summary>
    /// Множення Гамільтона (Quaternion Multiplication)
    /// </summary>
    public static Quaternion operator *(Quaternion q1, Quaternion q2)
    {
        int w = q1.W * q2.W - q1.X * q2.X - q1.Y * q2.Y - q1.Z * q2.Z;
        int x = q1.W * q2.X + q1.X * q2.W + q1.Y * q2.Z - q1.Z * q2.Y;
        int y = q1.W * q2.Y - q1.X * q2.Z + q1.Y * q2.W + q1.Z * q2.X;
        int z = q1.W * q2.Z + q1.X * q2.Y - q1.Y * q2.X + q1.Z * q2.W;

        return new Quaternion(w, x, y, z);
    }

    /// <summary>
    /// Норма кватерніона (Squared Norm)
    /// </summary>
    public long Norm()
    {
        return (long)W * W + (long)X * X + (long)Y * Y + (long)Z * Z;
    }

    /// <summary>
    /// Спряжений кватерніон (Conjugate)
    /// </summary>
    public Quaternion Conjugate()
    {
        return new Quaternion(W, -X, -Y, -Z);
    }

    /// <summary>
    /// Нормалізація за модулем 256 (для приведення до байтового діапазону)
    /// </summary>
    public Quaternion Normalize()
    {
        return new Quaternion(
            Mod(W, 256),
            Mod(X, 256),
            Mod(Y, 256),
            Mod(Z, 256)
        );
    }

    /// <summary>
    /// Перетворення в масив байтів
    /// </summary>
    public byte[] ToBytes()
    {
        var normalized = Normalize();
        return new byte[]
        {
            (byte)normalized.W,
            (byte)normalized.X,
            (byte)normalized.Y,
            (byte)normalized.Z
        };
    }

    /// <summary>
    /// Коректний модуль (завжди позитивний результат)
    /// </summary>
    private static int Mod(int a, int m)
    {
        int result = a % m;
        return result < 0 ? result + m : result;
    }

    /// <summary>
    /// Обчислення оберненого кватерніону за модулем
    /// </summary>
    public Quaternion ModularInverse(int modulus)
    {
        // Знайти обернений елемент для норми за модулем
        long norm = Norm();
        long normMod = Mod((int)(norm % modulus), modulus);

        // Розширений алгоритм Евкліда для знаходження оберненого
        long normInverse = ModInverse(normMod, modulus);

        // Спряжений кватерніон, помножений на обернену норму
        var conj = Conjugate();
        return new Quaternion(
            Mod((int)((conj.W * normInverse) % modulus), modulus),
            Mod((int)((conj.X * normInverse) % modulus), modulus),
            Mod((int)((conj.Y * normInverse) % modulus), modulus),
            Mod((int)((conj.Z * normInverse) % modulus), modulus)
        );
    }

    /// <summary>
    /// Знаходження оберненого елемента за модулем (Розширений алгоритм Евкліда)
    /// </summary>
    private static long ModInverse(long a, long m)
    {
        long m0 = m;
        long x0 = 0, x1 = 1;

        if (m == 1) return 0;

        while (a > 1)
        {
            long q = a / m;
            long t = m;

            m = a % m;
            a = t;
            t = x0;

            x0 = x1 - q * x0;
            x1 = t;
        }

        if (x1 < 0) x1 += m0;

        return x1;
    }

    public override string ToString()
    {
        return $"({W}, {X}, {Y}, {Z})";
    }
}
