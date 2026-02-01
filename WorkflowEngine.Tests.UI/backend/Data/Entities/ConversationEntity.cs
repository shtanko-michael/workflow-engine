using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkflowEngine.Tests.UI.Backend.Data.Entities;

[Table("conversations")]
public class ConversationEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = null!;

    [Column("title")]
    public string? Title { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("thread_id")]
    public string? ThreadId { get; set; }

    [Column("last_checkpoint_id")]
    public string? LastCheckpointId { get; set; }

    [Column("last_interrupt_request_id")]
    public string? LastInterruptRequestId { get; set; }

    [Column("active_leaf_message_id")]
    public string? ActiveLeafMessageId { get; set; }

    public MessageEntity? ActiveLeafMessage { get; set; }
    public ICollection<MessageEntity> Messages { get; set; } = new List<MessageEntity>();
}
