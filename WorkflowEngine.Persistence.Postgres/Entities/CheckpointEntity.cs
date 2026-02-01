using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkflowEngine.Persistence.Postgres.Entities;

/// <summary>
/// Checkpoint entity for PostgreSQL
/// </summary>
[Table("checkpoints")]
public class CheckpointEntity
{
    [Key, Column("thread_id", Order = 0)]
    public string ThreadId { get; set; } = string.Empty;
    
    [Key, Column("checkpoint_ns", Order = 1)]
    public string CheckpointNs { get; set; } = string.Empty;
    
    [Key, Column("checkpoint_id", Order = 2)]
    public string CheckpointId { get; set; } = string.Empty;
    
    [Column("parent_checkpoint_id")]
    public string? ParentCheckpointId { get; set; }
    
    [Column("type")]
    public string? Type { get; set; }
    
    [Column("checkpoint", TypeName = "jsonb")]
    public string CheckpointJson { get; set; } = string.Empty;
    
    [Column("metadata", TypeName = "jsonb")]
    public string MetadataJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
