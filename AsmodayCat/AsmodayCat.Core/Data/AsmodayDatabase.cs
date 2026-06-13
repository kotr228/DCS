using Microsoft.Data.Sqlite;

namespace AsmodayCat.Core.Data;

public class AsmodayDatabase
{
    public string ConnectionString { get; }

    public AsmodayDatabase()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatSuite", "AsmodayCat");
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "asmodaycat.db");
        ConnectionString = $"Data Source={dbPath}";
    }

    public SqliteConnection CreateConnection() =>
        new SqliteConnection(ConnectionString);
}
