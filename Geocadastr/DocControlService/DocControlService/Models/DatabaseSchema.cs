namespace DocControlService.Models
{
    public static class DatabaseSchema
    {
        public static readonly string[] CreateTables = new[]
        {
            @"
            CREATE TABLE IF NOT EXISTS directory (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(60) NOT NULL,
                Browse VARCHAR(200) NOT NULL
            );",
            @"
            CREATE TABLE IF NOT EXISTS Objects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(150) NOT NULL,
                inBrowse VARCHAR(150),
                idDirectory INT,
                FOREIGN KEY(idDirectory) REFERENCES directory(id)
            );",
            @"
            CREATE TABLE IF NOT EXISTS TypeFiles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                extention VARCHAR(45) NOT NULL,
                TypeName VARCHAR(150) NOT NULL
            );",
            @"
            CREATE TABLE IF NOT EXISTS Folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                NameFolder VARCHAR(150) NOT NULL,
                inBrowse VARCHAR(150),
                idObject INT,
                idDirectory INT,
                FOREIGN KEY(idObject) REFERENCES Objects(id),
                FOREIGN KEY(idDirectory) REFERENCES directory(id)
            );",
            @"
            CREATE TABLE IF NOT EXISTS Files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                NameFile VARCHAR(150) NOT NULL,
                inBrowse VARCHAR(150),
                idTypeFile INT,
                idFolder INT,
                idObject INT,
                idDirectory INT,
                FOREIGN KEY(idTypeFile) REFERENCES TypeFiles(id),
                FOREIGN KEY(idFolder) REFERENCES Folders(id),
                FOREIGN KEY(idObject) REFERENCES Objects(id),
                FOREIGN KEY(idDirectory) REFERENCES directory(id)
            );",
            @"
            CREATE TABLE IF NOT EXISTS DirectoryAccess (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                idDirectory INT NOT NULL,
                IsShared INTEGER NOT NULL DEFAULT 1, -- 1 = відкрито, 0 = закрито
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(idDirectory) REFERENCES directory(id)
            );"
        };
    }
}
