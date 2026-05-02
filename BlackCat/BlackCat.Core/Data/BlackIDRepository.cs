using Microsoft.Data.Sqlite;
using BlackCat.Shared.Models;

namespace BlackCat.Core.Data;

/// <summary>
/// Репозиторій для роботи з локальним Black-ID
/// </summary>
public class BlackIDRepository
{
    private readonly string _connectionString;

    public BlackIDRepository(BlackCatDatabase database)
    {
        _connectionString = database.ConnectionString;
    }

    /// <summary>
    /// Зберегти Black-ID
    /// </summary>
    public void SaveBlackID(BlackID blackID)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Спочатку деактивувати всі попередні ID
        string deactivateSql = "UPDATE LocalBlackID SET IsActive = 0";
        using (var deactivateCmd = new SqliteCommand(deactivateSql, connection))
        {
            deactivateCmd.ExecuteNonQuery();
        }

        // Знайти RoleId за назвою ролі
        int roleId = 1;
        string getRoleIdSql = "SELECT Id FROM Roles WHERE Name = @RoleName LIMIT 1";
        using (var getRoleCmd = new SqliteCommand(getRoleIdSql, connection))
        {
            getRoleCmd.Parameters.AddWithValue("@RoleName", blackID.Role);
            var result = getRoleCmd.ExecuteScalar();
            if (result != null)
            {
                roleId = Convert.ToInt32(result);
            }
        }

        // Знайти CityId за назвою міста
        int cityId = 1;
        string getCityIdSql = "SELECT Id FROM Cities WHERE Name = @CityName LIMIT 1";
        using (var getCityCmd = new SqliteCommand(getCityIdSql, connection))
        {
            getCityCmd.Parameters.AddWithValue("@CityName", blackID.City);
            var result = getCityCmd.ExecuteScalar();
            if (result != null)
            {
                cityId = Convert.ToInt32(result);
            }
        }

        // Вставити новий ID з FK та текстовими полями
        string insertSql = @"
            INSERT INTO LocalBlackID (FullID, RoleId, CityId, Role, City, Name, Code,
                                     HardwareFingerprint, Signature, CreatedAt, SignatureCreatedAt, IsActive)
            VALUES (@FullID, @RoleId, @CityId, @Role, @City, @Name, @Code,
                    @HardwareFingerprint, @Signature, @CreatedAt, @SignatureCreatedAt, @IsActive)
        ";

        using var command = new SqliteCommand(insertSql, connection);
        command.Parameters.AddWithValue("@FullID", blackID.FullID);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@CityId", cityId);
        command.Parameters.AddWithValue("@Role", blackID.Role);
        command.Parameters.AddWithValue("@City", blackID.City);
        command.Parameters.AddWithValue("@Name", blackID.Name);
        command.Parameters.AddWithValue("@Code", blackID.Code);
        command.Parameters.AddWithValue("@HardwareFingerprint", blackID.HardwareFingerprint);
        command.Parameters.AddWithValue("@Signature", blackID.Signature);
        command.Parameters.AddWithValue("@CreatedAt", blackID.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("@SignatureCreatedAt",
            (blackID.SignatureCreatedAt != default ? blackID.SignatureCreatedAt : blackID.CreatedAt).ToString("O"));
        command.Parameters.AddWithValue("@IsActive", blackID.IsActive ? 1 : 0);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Отримати поточний активний Black-ID
    /// </summary>
    public BlackID? GetActiveBlackID()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM LocalBlackID WHERE IsActive = 1 ORDER BY CreatedAt DESC LIMIT 1";
        using var command = new SqliteCommand(sql, connection);
        using var reader = command.ExecuteReader();

        if (reader.Read())
        {
            return MapToBlackID(reader);
        }

        return null;
    }

    /// <summary>
    /// Отримати всі Black-ID (історію)
    /// </summary>
    public List<BlackID> GetAllBlackIDs()
    {
        var list = new List<BlackID>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM LocalBlackID ORDER BY CreatedAt DESC";
        using var command = new SqliteCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(MapToBlackID(reader));
        }

        return list;
    }

    /// <summary>
    /// Видалити Black-ID
    /// </summary>
    public void DeleteBlackID(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "DELETE FROM LocalBlackID WHERE Id = @Id";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Мапити з reader в модель
    /// </summary>
    private BlackID MapToBlackID(SqliteDataReader reader)
    {
        DateTime signatureCreatedAt = DateTime.UtcNow;
        var signatureCreatedAtOrdinal = reader.GetOrdinal("SignatureCreatedAt");
        if (!reader.IsDBNull(signatureCreatedAtOrdinal))
        {
            signatureCreatedAt = DateTime.Parse(reader.GetString(signatureCreatedAtOrdinal));
        }

        int roleId = 1;
        var roleIdOrdinal = reader.GetOrdinal("RoleId");
        if (!reader.IsDBNull(roleIdOrdinal))
        {
            roleId = reader.GetInt32(roleIdOrdinal);
        }

        int cityId = 1;
        var cityIdOrdinal = reader.GetOrdinal("CityId");
        if (!reader.IsDBNull(cityIdOrdinal))
        {
            cityId = reader.GetInt32(cityIdOrdinal);
        }

        return new BlackID
        {
            FullID = reader.GetString(reader.GetOrdinal("FullID")),
            RoleId = roleId,
            CityId = cityId,
            Role = reader.GetString(reader.GetOrdinal("Role")),
            City = reader.GetString(reader.GetOrdinal("City")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Code = reader.GetString(reader.GetOrdinal("Code")),
            HardwareFingerprint = reader.GetString(reader.GetOrdinal("HardwareFingerprint")),
            Signature = reader.GetString(reader.GetOrdinal("Signature")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            SignatureCreatedAt = signatureCreatedAt,
            IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) == 1
        };
    }
}
