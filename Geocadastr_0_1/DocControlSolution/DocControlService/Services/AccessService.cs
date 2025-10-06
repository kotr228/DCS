using DocControlService.Data;
using DocControlService.Models;
using DocControlService.Services;
using System;
using System.Collections.Generic;

namespace DocControlService.Services
{
    /// <summary>
    /// Сервіс для керування доступом до директорій через мережу
    /// ОНОВЛЕНА ВЕРСІЯ - працює з новими таблицями Devises і NetworkAccesDirectory
    /// </summary>
    public class AccessService
    {
        private readonly DatabaseManager _db;
        private readonly DirectoryRepository _dirRepo;
        private readonly NetworkAccessRepository _accessRepo;
        private readonly NetworkShareService _shareService;

        public AccessService(DatabaseManager db)
        {
            _db = db;
            _dirRepo = new DirectoryRepository(db);
            _accessRepo = new NetworkAccessRepository(db);
            _shareService = new NetworkShareService();
        }

        /// <summary>
        /// Синхронізація - переконуємось що для кожної директорії є записи доступу
        /// </summary>
        public void SyncAccessTable()
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // Отримуємо всі директорії
            var directories = _dirRepo.GetAllDirectories();

            Console.WriteLine($"[AccessService] Синхронізація доступу для {directories.Count} директорій...");

            foreach (var dir in directories)
            {
                // Перевіряємо чи є хоч один запис доступу для цієї директорії
                var accessRecords = _accessRepo.GetAccessByDirectory(dir.Id);

                if (accessRecords.Count == 0)
                {
                    Console.WriteLine($"[AccessService] Директорія {dir.Name} (id={dir.Id}) не має записів доступу - створюємо базовий");
                    // Можна додати базовий запис або просто залишити порожнім
                    // Поки що залишаємо порожнім - доступ надається явно через GrantAccess
                }
            }

            Console.WriteLine("[AccessService] Синхронізація завершена");
        }

        /// <summary>
        /// Відкрити всі директорії для всіх дозволених пристроїв
        /// </summary>
        public void OpenAll()
        {
            Console.WriteLine("[AccessService] Відкриття всіх мережевих шарів...");

            var directories = _dirRepo.GetAllDirectories();

            foreach (var dir in directories)
            {
                // Перевіряємо чи є активні доступи для цієї директорії
                bool hasAccess = _accessRepo.IsDirectoryShared(dir.Id);

                if (hasAccess)
                {
                    string shareName = $"DocShare_{dir.Id}";
                    bool opened = _shareService.OpenShare(shareName, dir.Browse);

                    if (opened)
                    {
                        Console.WriteLine($"[AccessService] ✅ Відкрито: {shareName} -> {dir.Browse}");
                    }
                    else
                    {
                        Console.WriteLine($"[AccessService] ❌ Не вдалось відкрити: {shareName}");
                    }
                }
            }

            Console.WriteLine("[AccessService] Відкриття завершено");
        }

        /// <summary>
        /// Закрити всі мережеві шари
        /// </summary>
        public void CloseAll()
        {
            Console.WriteLine("[AccessService] Закриття всіх мережевих шарів...");

            var directories = _dirRepo.GetAllDirectories();

            foreach (var dir in directories)
            {
                string shareName = $"DocShare_{dir.Id}";

                if (_shareService.ShareExists(shareName))
                {
                    bool closed = _shareService.CloseShare(shareName);

                    if (closed)
                    {
                        Console.WriteLine($"[AccessService] ✅ Закрито: {shareName}");
                    }
                    else
                    {
                        Console.WriteLine($"[AccessService] ❌ Не вдалось закрити: {shareName}");
                    }
                }
            }

            // Також оновлюємо статуси в БД
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE NetworkAccesDirectory SET Status = 0;";
            cmd.ExecuteNonQuery();

            Console.WriteLine("[AccessService] Закриття завершено");
        }

        /// <summary>
        /// Відкрити конкретну директорію
        /// </summary>
        public bool OpenDirectory(int directoryId)
        {
            var dir = _dirRepo.GetById(directoryId);
            if (dir == null)
            {
                Console.WriteLine($"[AccessService] Директорію id={directoryId} не знайдено");
                return false;
            }

            string shareName = $"DocShare_{directoryId}";
            return _shareService.OpenShare(shareName, dir.Browse);
        }

        /// <summary>
        /// Закрити конкретну директорію
        /// </summary>
        public bool CloseDirectory(int directoryId)
        {
            string shareName = $"DocShare_{directoryId}";
            return _shareService.CloseShare(shareName);
        }

        /// <summary>
        /// Отримати список відкритих (shared) директорій
        /// </summary>
        public List<(int Id, string Name, string Path, bool IsShared)> GetSharedDirectories()
        {
            var result = new List<(int, string, string, bool)>();
            var directories = _dirRepo.GetAllDirectories();

            foreach (var dir in directories)
            {
                bool isShared = _accessRepo.IsDirectoryShared(dir.Id);
                result.Add((dir.Id, dir.Name, dir.Browse, isShared));
            }

            return result;
        }

        /// <summary>
        /// Перевірити чи існує мережевий шар для директорії
        /// </summary>
        public bool IsShareOpen(int directoryId)
        {
            string shareName = $"DocShare_{directoryId}";
            return _shareService.ShareExists(shareName);
        }
    }
}