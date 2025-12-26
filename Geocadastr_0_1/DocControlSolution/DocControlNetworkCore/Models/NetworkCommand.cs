using System;
using System.Collections.Generic;

namespace DocControlNetworkCore.Models
{
    /// <summary>
    /// Типи команд для мережевого обміну
    /// </summary>
    public enum CommandType
    {
        /// <summary>
        /// Запит списку файлів у директорії
        /// </summary>
        GetFileList,

        /// <summary>
        /// Запит метаданих файлу
        /// </summary>
        GetFileMeta,

        /// <summary>
        /// Запит на завантаження файлу
        /// </summary>
        DownloadFile,

        /// <summary>
        /// Heartbeat (сигнал активності)
        /// </summary>
        Heartbeat,

        /// <summary>
        /// Ping (перевірка доступності)
        /// </summary>
        Ping,

        /// <summary>
        /// Запит списку директорій, які цей вузол відкриває для доступу
        /// </summary>
        GetSharedDirectories,

        /// <summary>
        /// Відповідь на команду
        /// </summary>
        Response
    }

    /// <summary>
    /// Команда для мережевого обміну
    /// </summary>
    public class NetworkCommand
    {
        /// <summary>
        /// Тип команди
        /// </summary>
        public CommandType Type { get; set; }

        /// <summary>
        /// Ідентифікатор запиту (для зіставлення відповіді)
        /// </summary>
        public Guid RequestId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Дані команди (JSON серіалізовані)
        /// </summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>
        /// Мітка часу
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Відправник (для ідентифікації)
        /// </summary>
        public Guid SenderId { get; set; }
    }

    /// <summary>
    /// Відповідь на команду
    /// </summary>
    public class CommandResponse
    {
        /// <summary>
        /// Ідентифікатор запиту, на який це відповідь
        /// </summary>
        public Guid RequestId { get; set; }

        /// <summary>
        /// Чи успішна операція
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Повідомлення про помилку (якщо є)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Дані відповіді (JSON серіалізовані)
        /// </summary>
        public string? Data { get; set; }

        /// <summary>
        /// Мітка часу
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Запит на отримання списку файлів
    /// </summary>
    public class GetFileListRequest
    {
        /// <summary>
        /// Шлях до директорії
        /// </summary>
        public string DirectoryPath { get; set; } = string.Empty;

        /// <summary>
        /// Маска фільтру (*.txt, *.*, тощо)
        /// </summary>
        public string Filter { get; set; } = "*.*";

        /// <summary>
        /// Включити піддиректорії
        /// </summary>
        public bool IncludeSubdirectories { get; set; } = false;
    }

    /// <summary>
    /// Метадані файлу
    /// </summary>
    public class FileMetadata
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public bool IsDirectory { get; set; }
        public string Extension { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запит на завантаження файлу
    /// </summary>
    public class DownloadFileRequest
    {
        /// <summary>
        /// Повний шлях до файлу
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Зміщення (для підтримки докачування)
        /// </summary>
        public long Offset { get; set; } = 0;

        /// <summary>
        /// Розмір буфера
        /// </summary>
        public int BufferSize { get; set; } = 8192;
    }
}
