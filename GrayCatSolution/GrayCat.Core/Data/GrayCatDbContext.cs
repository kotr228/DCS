namespace GrayCat.Core.Data;

using GrayCat.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

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

            // Ignore complex properties for SQLite
            entity.Ignore(e => e.Properties);
            entity.Ignore(e => e.Styles);
        });

        // Configure ProjectModel
        modelBuilder.Entity<ProjectModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProjectName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Author).HasMaxLength(200);

            // Ignore complex properties
            entity.Ignore(e => e.Blocks);
            entity.Ignore(e => e.Settings);
        });
    }
}