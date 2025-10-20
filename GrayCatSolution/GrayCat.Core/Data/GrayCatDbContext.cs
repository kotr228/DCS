namespace GrayCat.Core.Data;

using GrayCat.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public class GrayCatDbContext : DbContext
{
    public DbSet<ProjectModel> Projects { get; set; }
    public DbSet<BlockModel> Blocks { get; set; }

    public GrayCatDbContext(DbContextOptions<GrayCatDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure BlockModel
        modelBuilder.Entity<BlockModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Content).HasMaxLength(5000);
            entity.Property(e => e.Color).HasMaxLength(20);
            entity.Property(e => e.Background).HasMaxLength(20);
            entity.Property(e => e.FontFamily).HasMaxLength(100);
            entity.Property(e => e.ImagePath).HasMaxLength(500);

            // NEW in v0.3.4: JSON columns for dynamic properties
            entity.Property(e => e.PropertiesJson)
                .HasColumnName("PropertiesJson")
                .HasColumnType("TEXT");

            entity.Property(e => e.StylesJson)
                .HasColumnName("StylesJson")
                .HasColumnType("TEXT");

            entity.Property(e => e.BlockVersion)
                .HasMaxLength(20)
                .HasDefaultValue("0.3.4");

            // Ignore complex properties (they're stored as JSON)
            entity.Ignore(e => e.Properties);
            entity.Ignore(e => e.Styles);

            // Add indexes for performance
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.ModifiedAt);
        });

        // Configure ProjectModel
        modelBuilder.Entity<ProjectModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProjectName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Author).HasMaxLength(200);
            entity.Property(e => e.Version).HasMaxLength(20).HasDefaultValue("1.0.0");

            // Ignore complex properties
            entity.Ignore(e => e.Blocks);
            entity.Ignore(e => e.Settings);

            // Add indexes
            entity.HasIndex(e => e.ProjectName);
            entity.HasIndex(e => e.CreatedAt);
        });
    }

    /// <summary>
    /// Override SaveChanges to serialize Properties and Styles to JSON
    /// </summary>
    public override int SaveChanges()
    {
        SerializeComplexProperties();
        return base.SaveChanges();
    }

    /// <summary>
    /// Override SaveChangesAsync to serialize Properties and Styles to JSON
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SerializeComplexProperties();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void SerializeComplexProperties()
    {
        var entries = ChangeTracker.Entries<BlockModel>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            var block = entry.Entity;

            // Serialize Properties to JSON
            if (block.Properties != null && block.Properties.Any())
            {
                block.PropertiesJson = JsonSerializer.Serialize(block.Properties, new JsonSerializerOptions
                {
                    WriteIndented = false
                });
            }
            else
            {
                block.PropertiesJson = "{}";
            }

            // Serialize Styles to JSON
            if (block.Styles != null && block.Styles.Any())
            {
                block.StylesJson = JsonSerializer.Serialize(block.Styles, new JsonSerializerOptions
                {
                    WriteIndented = false
                });
            }
            else
            {
                block.StylesJson = "{}";
            }
        }
    }
}

/// <summary>
/// Extension methods for BlockModel to handle JSON serialization/deserialization
/// </summary>
public static class BlockModelExtensions
{
    /// <summary>
    /// Load Properties from PropertiesJson
    /// </summary>
    public static void LoadPropertiesFromJson(this BlockModel block)
    {
        if (!string.IsNullOrWhiteSpace(block.PropertiesJson))
        {
            try
            {
                var properties = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    block.PropertiesJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (properties != null)
                {
                    block.Properties = properties;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to deserialize Properties: {ex.Message}");
                block.Properties = new Dictionary<string, object>();
            }
        }
    }

    /// <summary>
    /// Load Styles from StylesJson
    /// </summary>
    public static void LoadStylesFromJson(this BlockModel block)
    {
        if (!string.IsNullOrWhiteSpace(block.StylesJson))
        {
            try
            {
                var styles = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    block.StylesJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (styles != null)
                {
                    block.Styles = styles;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to deserialize Styles: {ex.Message}");
                block.Styles = new Dictionary<string, string>();
            }
        }
    }

    /// <summary>
    /// Load both Properties and Styles from JSON
    /// </summary>
    public static void LoadFromJson(this BlockModel block)
    {
        block.LoadPropertiesFromJson();
        block.LoadStylesFromJson();
    }
}