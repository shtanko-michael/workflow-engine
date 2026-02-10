using WorkflowEngine.Tests.UI.Backend.Data.Entities;

namespace WorkflowEngine.Tests.UI.Backend.Data.Repositories;

/// <summary>
/// For UI: message at a position with possible sibling versions (alternatives).
/// </summary>
public class MessageWithAlternatives
{
    public MessageEntity ActiveMessage { get; set; } = null!;
    public List<MessageEntity> Alternatives { get; set; } = new();
    public int CurrentIndex { get; set; }
    public int TotalAlternatives { get; set; }
}

public interface IMessageRepository
{
    /// <summary>Branch from root to leaf (inclusive), ordered by created_at.</summary>
    Task<List<MessageEntity>> GetBranchToLeafAsync(string conversationId, string? leafMessageId);

    Task<MessageEntity?> GetMessageAsync(string messageId);

    /// <summary>Children of a message (alternatives at next position).</summary>
    Task<List<MessageEntity>> GetChildrenAsync(string parentMessageId);

    /// <summary>Leaves of the conversation (messages with no children).</summary>
    Task<List<MessageEntity>> GetLeavesAsync(string conversationId);

    /// <summary>Leaf of the branch that contains the given message (for switch-version).</summary>
    Task<MessageEntity?> GetLeafOfBranchContainingAsync(string conversationId, string messageId);

    Task<MessageEntity> CreateMessageAsync(
        string conversationId,
        string? parentId,
        string role,
        string content,
        string? checkpointId = null,
        string? requestId = null,
        string? checkpointNs = null);

    /// <summary>Edit = insert new message with same parent and new content (new branch).</summary>
    Task<MessageEntity> CreateSiblingAsync(MessageEntity editedMessage, string newContent);

    // Task UpdateActiveLeafAsync(string conversationId, string leafMessageId);

    Task UpdateContentAsync(string messageId, string content);

    /// <summary>Updates options (quick-reply choices) for an assistant message. Stored as JSON array.</summary>
    Task UpdateOptionsAsync(string messageId, string[]? options);

    Task UpdateCheckpointAsync(string messageId, string checkpointId);
}
