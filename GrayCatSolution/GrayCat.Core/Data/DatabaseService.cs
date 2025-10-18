namespace GrayCat.Core.Data;

using Microsoft.EntityFrameworkCore;
using GrayCat.Core.Models;
using GrayCat.Core.Constants;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        var dbPath = Path.Combine(AppConstants.Paths.AppData, "graycat.db");
        _connectionString = $"Data Source={dbPath}";

        // Ensure database exists
        EnsureDatabase();
    }

    private void EnsureDatabase()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    private GrayCatDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<GrayCatDbContext>();
        optionsBuilder.UseSqlite(_connectionString);
        return new GrayCatDbContext(optionsBuilder.Options);
    }

    // Block Operations
    public async Task<BlockModel?> GetBlockAsync(string id)
    {
        using var context = CreateContext();
        return await context.Blocks.FindAsync(id);
    }

    public async Task<List<BlockModel>> GetProjectBlocksAsync(string projectId)
    {
        using var context = CreateContext();
        return await context.Blocks
            .Where(b => b.Id.StartsWith(projectId))
            .ToListAsync();
    }

    public async Task SaveBlockAsync(BlockModel block)
    {
        using var context = CreateContext();

        block.ModifiedAt = DateTime.UtcNow;

        var existing = await context.Blocks.FindAsync(block.Id);
        if (existing != null)
        {
            context.Entry(existing).CurrentValues.SetValues(block);
        }
        else
        {
            await context.Blocks.AddAsync(block);
        }

        await context.SaveChangesAsync();
    }

    public async Task DeleteBlockAsync(string id)
    {
        using var context = CreateContext();
        var block = await context.Blocks.FindAsync(id);
        if (block != null)
        {
            context.Blocks.Remove(block);
            await context.SaveChangesAsync();
        }
    }

    // Project Operations
    public async Task SaveProjectMetadataAsync(ProjectModel project)
    {
        using var context = CreateContext();

        var existing = await context.Projects.FindAsync(project.Id);
        if (existing != null)
        {
            context.Entry(existing).CurrentValues.SetValues(project);
        }
        else
        {
            await context.Projects.AddAsync(project);
        }

        await context.SaveChangesAsync();
    }
}