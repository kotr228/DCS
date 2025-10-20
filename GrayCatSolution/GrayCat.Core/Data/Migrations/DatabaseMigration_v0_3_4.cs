namespace GrayCat.Core.Data.Migrations;

using GrayCat.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

/// <summary>
/// Database migration for v0.3.4 - adds support for new block types with dynamic properties
/// </summary>
public class DatabaseMigration_v0_3_4
{
    private readonly GrayCatDbContext _context;

    public DatabaseMigration_v0_3_4(GrayCatDbContext context)
    {
        _context = context;
    }

    public async Task MigrateAsync()
    {
        try
        {
            // Check if migration is needed
            if (!await IsMigrationNeededAsync())
            {
                Console.WriteLine("Database is already up to date (v0.3.4)");
                return;
            }

            Console.WriteLine("Starting database migration to v0.3.4...");

            // Step 1: Add PropertiesJson column if it doesn't exist
            await AddPropertiesJsonColumnAsync();

            // Step 2: Migrate existing blocks to new format
            await MigrateExistingBlocksAsync();

            // Step 3: Update version metadata
            await UpdateDatabaseVersionAsync();

            Console.WriteLine("Migration to v0.3.4 completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Migration failed: {ex.Message}");
            throw;
        }
    }

    private async Task<bool> IsMigrationNeededAsync()
    {
        // Check if PropertiesJson column exists in Blocks table
        var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(Blocks)";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var columnName = reader.GetString(1);
            if (columnName == "PropertiesJson")
            {
                return false; // Column exists, migration not needed
            }
        }

        return true; // Migration needed
    }

    private async Task AddPropertiesJsonColumnAsync()
    {
        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Blocks ADD COLUMN PropertiesJson TEXT");

            Console.WriteLine("✓ Added PropertiesJson column");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Note: PropertiesJson column might already exist: {ex.Message}");
        }

        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Blocks ADD COLUMN StylesJson TEXT");

            Console.WriteLine("✓ Added StylesJson column");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Note: StylesJson column might already exist: {ex.Message}");
        }

        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Blocks ADD COLUMN BlockVersion TEXT DEFAULT '0.3.4'");

            Console.WriteLine("✓ Added BlockVersion column");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Note: BlockVersion column might already exist: {ex.Message}");
        }
    }

    private async Task MigrateExistingBlocksAsync()
    {
        var blocks = await _context.Blocks.ToListAsync();
        var migratedCount = 0;

        foreach (var block in blocks)
        {
            // Migrate Properties dictionary to JSON
            if (block.Properties != null && block.Properties.Any())
            {
                var propertiesJson = JsonSerializer.Serialize(block.Properties);

                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE Blocks SET PropertiesJson = {0} WHERE Id = {1}",
                    propertiesJson, block.Id);
            }

            // Migrate Styles dictionary to JSON
            if (block.Styles != null && block.Styles.Any())
            {
                var stylesJson = JsonSerializer.Serialize(block.Styles);

                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE Blocks SET StylesJson = {0} WHERE Id = {1}",
                    stylesJson, block.Id);
            }

            // Set default properties for new block types
            await SetDefaultPropertiesForBlockTypeAsync(block);

            migratedCount++;
        }

        Console.WriteLine($"✓ Migrated {migratedCount} blocks to new format");
    }

    private async Task SetDefaultPropertiesForBlockTypeAsync(BlockModel block)
    {
        var defaultProperties = GetDefaultPropertiesForBlockType(block.Type);

        if (defaultProperties == null || !defaultProperties.Any())
            return;

        // Merge with existing properties
        var existingProps = block.Properties ?? new Dictionary<string, object>();

        foreach (var kvp in defaultProperties)
        {
            if (!existingProps.ContainsKey(kvp.Key))
            {
                existingProps[kvp.Key] = kvp.Value;
            }
        }

        var propertiesJson = JsonSerializer.Serialize(existingProps);

        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE Blocks SET PropertiesJson = {0} WHERE Id = {1}",
            propertiesJson, block.Id);
    }

    private Dictionary<string, object>? GetDefaultPropertiesForBlockType(string blockType)
    {
        return blockType switch
        {
            "Button" => new Dictionary<string, object>
            {
                { "text", "Click Me" },
                { "url", "#" },
                { "variant", "primary" },
                { "size", "medium" }
            },
            "Hero" => new Dictionary<string, object>
            {
                { "headline", "Welcome" },
                { "subheadline", "" },
                { "ctaText", "Get Started" },
                { "ctaUrl", "#" }
            },
            "Card" => new Dictionary<string, object>
            {
                { "title", "Card Title" },
                { "description", "" },
                { "linkText", "Learn More" },
                { "linkUrl", "#" }
            },
            "Navbar" => new Dictionary<string, object>
            {
                { "brandName", "Brand" },
                { "position", "static" },
                { "menuItems", "[]" }
            },
            "ContactForm" => new Dictionary<string, object>
            {
                { "title", "Contact Us" },
                { "submitUrl", "/api/contact" },
                { "submitText", "Send" }
            },
            "Video" => new Dictionary<string, object>
            {
                { "videoUrl", "" },
                { "provider", "youtube" },
                { "controls", true }
            },
            "Testimonial" => new Dictionary<string, object>
            {
                { "quote", "" },
                { "author", "" },
                { "role", "" },
                { "rating", 5 }
            },
            "Pricing" => new Dictionary<string, object>
            {
                { "planName", "Plan" },
                { "price", "0" },
                { "currency", "$" },
                { "features", "[]" }
            },
            "Accordion" => new Dictionary<string, object>
            {
                { "title", "FAQ" },
                { "items", "[]" }
            },
            "Stats" => new Dictionary<string, object>
            {
                { "stats", "[]" },
                { "layout", "horizontal" }
            },
            "SocialLinks" => new Dictionary<string, object>
            {
                { "links", "[]" },
                { "style", "icons" }
            },
            "Divider" => new Dictionary<string, object>
            {
                { "style", "solid" },
                { "thickness", 1 },
                { "color", "#DDDDDD" }
            },
            _ => null
        };
    }

    private async Task UpdateDatabaseVersionAsync()
    {
        // Create or update version metadata
        var versionTableExists = await CheckVersionTableExistsAsync();

        if (!versionTableExists)
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS DatabaseVersion (
                    Id INTEGER PRIMARY KEY,
                    Version TEXT NOT NULL,
                    MigratedAt TEXT NOT NULL
                )");
        }

        await _context.Database.ExecuteSqlRawAsync(@"
            INSERT OR REPLACE INTO DatabaseVersion (Id, Version, MigratedAt)
            VALUES (1, '0.3.4', {0})",
            DateTime.UtcNow.ToString("O"));

        Console.WriteLine("✓ Updated database version to 0.3.4");
    }

    private async Task<bool> CheckVersionTableExistsAsync()
    {
        var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT name FROM sqlite_master 
            WHERE type='table' AND name='DatabaseVersion'";

        var result = await command.ExecuteScalarAsync();
        return result != null;
    }

    /// <summary>
    /// Rollback migration if something goes wrong
    /// </summary>
    public async Task RollbackAsync()
    {
        Console.WriteLine("Rolling back migration...");

        try
        {
            // Remove added columns (SQLite doesn't support DROP COLUMN easily)
            // Instead, we'll create a new table and copy data

            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS Blocks_Backup AS 
                SELECT * FROM Blocks");

            Console.WriteLine("✓ Created backup table");

            // Note: Full rollback would require recreating the table without new columns
            // This is left as a TODO for production use

            Console.WriteLine("⚠ Rollback partially completed. Manual intervention may be required.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Rollback failed: {ex.Message}");
            throw;
        }
    }
}

/// <summary>
/// Migration runner - execute this on application startup
/// </summary>
public class MigrationRunner
{
    public static async Task RunMigrationsAsync(GrayCatDbContext context)
    {
        try
        {
            Console.WriteLine("Checking for database migrations...");

            var migration = new DatabaseMigration_v0_3_4(context);
            await migration.MigrateAsync();

            Console.WriteLine("All migrations completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Migration failed: {ex.Message}");
            Console.WriteLine("The application will continue with existing database schema.");
        }
    }
}