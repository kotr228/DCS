using DocControlService.Models;
using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocControlService.Data
{
    public class DeviceRepository
    {
        private readonly DatabaseManager _db;

        public DeviceRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public List<DeviceModel> GetAllDevices()
        {
            var result = new List<DeviceModel>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, Name, Acces FROM Devises ORDER BY id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DeviceModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Access = reader.GetBoolean(2)
                });
            }
            return result;
        }

        public DeviceModel GetById(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, Name, Acces FROM Devises WHERE id = @id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new DeviceModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Access = reader.GetBoolean(2)
                };
            }
            return null;
        }

        public int AddDevice(string name, bool access = false)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var txn = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = "INSERT INTO Devises (Name, Acces) VALUES (@name, @access);";
            cmd.Parameters.AddWithValue("@name", name ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@access", access);
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            long id = (long)cmd.ExecuteScalar();
            txn.Commit();
            return (int)id;
        }

        public bool UpdateDevice(int id, string name, bool access)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Devises SET Name = @name, Acces = @access WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@name", name ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@access", access);
            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }

        public bool DeleteDevice(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Devises WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }
    }
}
