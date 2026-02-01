using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkflowEngine.Persistence.Postgres.Entities;

/// <summary>
/// Checkpoint blob entity for PostgreSQL
/// </summary>
[Table("checkpoint_blobs")]
public class CheckpointBlobEntity
{
    [Key, Column("thread_id", Order = 0)]
    public string ThreadId { get; set; } = string.Empty;
    
    [Key, Column("checkpoint_ns", Order = 1)]
    public string CheckpointNs { get; set; } = string.Empty;
    
    [Key, Column("channel", Order = 2)]
    public string Channel { get; set; } = string.Empty;
    
    [Key, Column("version", Order = 3)]
    public string Version { get; set; } = string.Empty;
    
    [Column("type")]
    public string Type { get; set; } = string.Empty;
    
    [Column("blob", TypeName = "bytea")]
    public byte[]? Blob { get; set; }
}
