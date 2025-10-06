﻿// File: Data/DirectoryRepository.cs
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using DocControlService.Models;

namespace DocControlService.Data
{
    public class DirectoryRepository
    {
        private readonly DatabaseManager _db;

        public DirectoryRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public List<DirectoryModel> GetAllDirectories()
        {
            var result = new List<DirectoryModel>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, Name, Browse FROM directory ORDER BY id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DirectoryModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Browse = reader.GetString(2)
                });
            }
            return result;
        }

        public DirectoryModel GetByBrowse(string browsePath)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, Name, Browse FROM directory WHERE Browse = @browse LIMIT 1;";
            cmd.Parameters.AddWithValue("@browse", browsePath);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new DirectoryModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Browse = reader.GetString(2)
                };
            }
            return null;
        }

        public DirectoryModel GetById(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, Name, Browse FROM directory WHERE id = @id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new DirectoryModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Browse = reader.GetString(2)
                };
            }
            return null;
        }

        // повертає id доданого запису
        public int AddDirectory(string name, string browse)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var txn = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = "INSERT INTO directory (Name, Browse) VALUES (@name, @browse);";
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@browse", browse);
            cmd.ExecuteNonQuery();

            // last_insert_rowid()
            cmd.CommandText = "SELECT last_insert_rowid();";
            long id = (long)cmd.ExecuteScalar();
            txn.Commit();
            return (int)id;
        }

        public bool DeleteDirectory(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM directory WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }
    }
}
