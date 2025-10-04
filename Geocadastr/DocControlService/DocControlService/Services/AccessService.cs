using DocControlService.Data;
using DocControlService.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DocControlService.Services
{
    public class AccessService
    {
        private readonly DatabaseManager _db;
        private readonly NetworkShareService _shareService;

        public AccessService(DatabaseManager db)
        {
            _db = db;
            _shareService = new NetworkShareService();
        }

        /// <summary>
        /// Синхронізація таблиці DirectoryAccess з directory
        /// </summary>
        public void SyncAccessTable()
        {
            using var conn = _db.GetConnection();
            conn.Open();

            var directoryIds = new List<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM directory;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    directoryIds.Add(reader.GetInt32(0));
                }
            }

            var accessIds = new HashSet<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT idDirectory FROM DirectoryAccess;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    accessIds.Add(reader.GetInt32(0));
                }
            }

            foreach (var id in directoryIds)
            {
                if (!accessIds.Contains(id))
                {
                    using var insertCmd = conn.CreateCommand();
                    insertCmd.CommandText = @"
                        INSERT INTO DirectoryAccess (idDirectory, IsShared) 
                        VALUES ($idDirectory, 1);";
                    insertCmd.Parameters.AddWithValue("$idDirectory", id);
                    insertCmd.ExecuteNonQuery();

                    Console.WriteLine($"📌 Додано запис у DirectoryAccess для directory.id={id}");
                }
            }
        }

        /// <summary>
        /// Відкрити доступ до всіх директорій
        /// </summary>
        public void OpenAll()
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE DirectoryAccess SET IsShared = 1;";
                cmd.ExecuteNonQuery();
            }

            var sharedDirs = GetAllDirectories();
            foreach (var d in sharedDirs)
            {
                string shareName = $"Dir_{d.Id}";
                _shareService.OpenShare(shareName, d.Path);
            }
        }

        /// <summary>
        /// Закрити доступ до всіх директорій
        /// </summary>
        public void CloseAll()
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE DirectoryAccess SET IsShared = 0;";
                cmd.ExecuteNonQuery();
            }

            var allDirs = GetAllDirectories();
            foreach (var d in allDirs)
            {
                string shareName = $"Dir_{d.Id}";
                _shareService.CloseShare(shareName);
            }
        }

        /// <summary>
        /// Отримати список відкритих директорій
        /// </summary>
        public List<(int Id, string Name, string Path)> GetSharedDirectories()
        {
            var result = new List<(int, string, string)>();

            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT d.id, d.Name, d.Browse 
                FROM directory d
                INNER JOIN DirectoryAccess a ON d.id = a.idDirectory
                WHERE a.IsShared = 1;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2)
                ));
            }

            return result;
        }

        /// <summary>
        /// Отримати всі директорії
        /// </summary>
        private List<(int Id, string Name, string Path)> GetAllDirectories()
        {
            var result = new List<(int, string, string)>();

            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT id, Name, Browse FROM directory;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2)
                ));
            }

            return result;
        }
    }
}
