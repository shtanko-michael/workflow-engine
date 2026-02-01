using Microsoft.EntityFrameworkCore;
using WorkflowEngine.Persistence.Postgres.Entities;

namespace WorkflowEngine.Persistence.Postgres;

/// <summary>
/// DbContext for checkpoint persistence
/// </summary>
public class CheckpointDbContext : DbContext
{
    public DbSet<CheckpointEntity> Checkpoints { get; set; } = null!;
    public DbSet<CheckpointBlobEntity> CheckpointBlobs { get; set; } = null!;
    public DbSet<CheckpointMigrationEntity> CheckpointMigrations { get; set; } = null!;
    
    public CheckpointDbContext(DbContextOptions<CheckpointDbContext> options)
        : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure CheckpointEntity composite key
        modelBuilder.Entity<CheckpointEntity>()
            .HasKey(e => new { e.ThreadId, e.CheckpointNs, e.CheckpointId });
        
        // Configure CheckpointBlobEntity composite key
        modelBuilder.Entity<CheckpointBlobEntity>()
            .HasKey(e => new { e.ThreadId, e.CheckpointNs, e.Channel, e.Version });
        
        // Configure JSONB columns for PostgreSQL
        modelBuilder.Entity<CheckpointEntity>()
            .Property(e => e.CheckpointJson)
            .HasColumnType("jsonb");
            
        modelBuilder.Entity<CheckpointEntity>()
            .Property(e => e.MetadataJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<CheckpointEntity>()
            .Property(e => e.CreatedAt)
            .HasDefaultValueSql("now()");

        modelBuilder.Entity<CheckpointEntity>()
            .HasIndex(e => new { e.ThreadId, e.CheckpointNs, e.CreatedAt });
    }
}
