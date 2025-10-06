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
            );",
@"
CREATE TABLE IF NOT EXISTS Devises (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT,
    Acces INTEGER NOT NULL DEFAULT 0
);",
@"
CREATE TABLE IF NOT EXISTS NetworkAccesDirectory (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    idDyrectory INTEGER,
    Status INTEGER NOT NULL DEFAULT 0,
    idDevises INTEGER,
    FOREIGN KEY(idDyrectory) REFERENCES directory(id),
    FOREIGN KEY(idDevises) REFERENCES Devises(id)
);",
    @"
    CREATE TABLE IF NOT EXISTS CommitStatusLog (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        directoryId INTEGER NOT NULL,
        directoryPath TEXT NOT NULL,
        status TEXT NOT NULL,
        message TEXT,
        timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
        FOREIGN KEY(directoryId) REFERENCES directory(id)
    );",
    @"
    CREATE TABLE IF NOT EXISTS ErrorLog (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        errorType TEXT NOT NULL,
        errorMessage TEXT NOT NULL,
        userFriendlyMessage TEXT NOT NULL,
        stackTrace TEXT,
        timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
        isResolved INTEGER DEFAULT 0
    );",
    @"
    CREATE TABLE IF NOT EXISTS AppSettings (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        settingKey TEXT NOT NULL UNIQUE,
        settingValue TEXT NOT NULL,
        description TEXT,
        updatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
    );",
@"
CREATE TABLE IF NOT EXISTS Roadmaps (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    directoryId INTEGER NOT NULL,
    name TEXT NOT NULL,
    description TEXT,
    createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(directoryId) REFERENCES directory(id)
);",

@"
CREATE TABLE IF NOT EXISTS RoadmapEvents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    roadmapId INTEGER NOT NULL,
    title TEXT NOT NULL,
    description TEXT,
    eventDate DATETIME NOT NULL,
    eventType TEXT NOT NULL,
    filePath TEXT,
    category TEXT,
    FOREIGN KEY(roadmapId) REFERENCES Roadmaps(id) ON DELETE CASCADE
);",

@"
CREATE TABLE IF NOT EXISTS ExternalServices (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    serviceType TEXT NOT NULL,
    url TEXT NOT NULL,
    apiKey TEXT,
    isActive INTEGER DEFAULT 1,
    lastUsed DATETIME,
    createdAt DATETIME DEFAULT CURRENT_TIMESTAMP
);"
        };
    }
}
