using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkflowEngine.Tests.UI.Backend.Data.Entities;

[Table("messages")]
public class MessageEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = null!;

    [Required]
    [Column("conversation_id")]
    public string ConversationId { get; set; } = null!;

    [Column("parent_id")]
    public string? ParentId { get; set; }

    [Required]
    [Column("role")]
    public string Role { get; set; } = null!; // "user", "assistant", "system"

    [Required]
    [Column("content")]
    public string Content { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("checkpoint_id")]
    public string? CheckpointId { get; set; }

    [Column("checkpoint_ns")]
    public string? CheckpointNs { get; set; }

    [Column("request_id")]
    public string? RequestId { get; set; }

    /// <summary>
    /// JSON array of quick-reply options for assistant messages (e.g. ["Option A", "Option B"]).
    /// </summary>
    [Column("options")]
    public string? Options { get; set; }

    public ConversationEntity Conversation { get; set; } = null!;
    public MessageEntity? Parent { get; set; }
    public ICollection<MessageEntity> Children { get; set; } = new List<MessageEntity>();
}
