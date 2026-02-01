using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkflowEngine.Persistence.Postgres.Entities;

/// <summary>
/// Checkpoint migration entity for tracking migrations
/// </summary>
[Table("checkpoint_migrations")]
public class CheckpointMigrationEntity
{
    [Key, Column("v")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Version { get; set; }
}
