using System;

namespace DocControlNetworkCore.Models
{
    /// <summary>
    /// Паспорт користувача - ідентифікація вузла в мережі
    /// </summary>
    public class PeerIdentity
    {
        /// <summary>
        /// Унікальний ідентифікатор екземпляра (генерується один раз при першому запуску)
        /// </summary>
        public Guid InstanceId { get; set; }

        /// <summary>
        /// Ім'я користувача
        /// </summary>
        public string UserName { get; set; } = string.Empty;

        /// <summary>
        /// Ім'я комп'ютера
        /// </summary>
        public string MachineName { get; set; } = string.Empty;

        /// <summary>
        /// Поточна локальна IP-адреса
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>
        /// Порт TCP сервера для прийому команд
        /// </summary>
        public int TcpPort { get; set; }

        /// <summary>
        /// Порт UDP для Discovery
        /// </summary>
        public int UdpPort { get; set; }

        /// <summary>
        /// Час останньої активності (для Heartbeat)
        /// </summary>
        public DateTime LastSeen { get; set; } = DateTime.Now;

        /// <summary>
        /// Версія протоколу
        /// </summary>
        public string ProtocolVersion { get; set; } = "1.0";

        /// <summary>
        /// Перевірка чи це цей же екземпляр (щоб не знаходити самого себе)
        /// </summary>
        public bool IsSelf(Guid localInstanceId)
        {
            return InstanceId == localInstanceId;
        }

        public override string ToString()
        {
            return $"{UserName}@{MachineName} ({IpAddress}:{TcpPort})";
        }
    }
}
