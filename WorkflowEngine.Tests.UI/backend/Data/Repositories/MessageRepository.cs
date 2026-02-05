using Microsoft.EntityFrameworkCore;
using WorkflowEngine.Tests.UI.Backend.Data.Entities;

namespace WorkflowEngine.Tests.UI.Backend.Data.Repositories;

public class MessageRepository : IMessageRepository
{
    private readonly ApplicationDbContext _context;

    public MessageRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<MessageEntity>> GetBranchToLeafAsync(string conversationId, string? leafMessageId)
    {
        if (string.IsNullOrEmpty(leafMessageId))
            return new List<MessageEntity>();

        var path = new List<MessageEntity>();
        var currentId = leafMessageId;

        while (!string.IsNullOrEmpty(currentId))
        {
            var msg = await _context.Messages
                .FirstOrDefaultAsync(m => m.Id == currentId && m.ConversationId == conversationId);
            if (msg == null)
                break;
            path.Add(msg);
            currentId = msg.ParentId;
        }

        path.Reverse();
        return path;
    }

    public async Task<MessageEntity?> GetMessageAsync(string messageId)
    {
        return await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
    }

    public async Task<List<MessageEntity>> GetChildrenAsync(string parentMessageId)
    {
        return await _context.Messages
            .Where(m => m.ParentId == parentMessageId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<MessageEntity>> GetLeavesAsync(string conversationId)
    {
        return await _context.Messages
            .Where(m => m.ConversationId == conversationId &&
                        !_context.Messages.Any(c => c.ParentId == m.Id))
            .ToListAsync();
    }

    public async Task<MessageEntity?> GetLeafOfBranchContainingAsync(string conversationId, string messageId)
    {
        var current = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId);
        if (current == null)
            return null;

        while (true)
        {
            var child = await _context.Messages
                .Where(m => m.ParentId == current.Id)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();
            if (child == null)
                return current;
            current = child;
        }
    }

    public async Task<MessageEntity> CreateMessageAsync(
        string conversationId,
        string? parentId,
        string role,
        string content,
        string? checkpointId = null,
        string? requestId = null,
        string? checkpointNs = null)
    {
        var msg = new MessageEntity
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            ParentId = parentId,
            Role = role,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            CheckpointId = checkpointId,
            CheckpointNs = checkpointNs,
            RequestId = requestId
        };
        _context.Messages.Add(msg);
        await _context.SaveChangesAsync();
        return msg;
    }

    public async Task<MessageEntity> CreateSiblingAsync(string editedMessageId, string newContent)
    {
        var edited = await _context.Messages.FirstOrDefaultAsync(m => m.Id == editedMessageId);
        if (edited == null)
            throw new InvalidOperationException($"Message {editedMessageId} not found");

        var id = Guid.NewGuid().ToString();
        var msg = new MessageEntity
        {
            Id = id,
            ConversationId = edited.ConversationId,
            ParentId = edited.ParentId,
            Role = edited.Role,
            Content = newContent,
            CreatedAt = DateTime.UtcNow,
            CheckpointId = edited.CheckpointId,
            CheckpointNs = id,
            RequestId = edited.RequestId
        };
        _context.Messages.Add(msg);
        await _context.SaveChangesAsync();
        return msg;
    }

    // public async Task UpdateActiveLeafAsync(string conversationId, string leafMessageId)
    // {
    //     var conv = await _context.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId);
    //     if (conv == null)
    //         throw new InvalidOperationException($"Conversation {conversationId} not found");

    //     var msg = await _context.Messages
    //         .FirstOrDefaultAsync(m => m.Id == leafMessageId && m.ConversationId == conversationId);
    //     if (msg == null)
    //         throw new InvalidOperationException($"Message {leafMessageId} not found in conversation");

    //     conv.ActiveLeafMessageId = leafMessageId;
    //     conv.UpdatedAt = DateTime.UtcNow;
    //     _context.Conversations.Update(conv);
    //     await _context.SaveChangesAsync();
    // }

    public async Task UpdateContentAsync(string messageId, string content)
    {
        var msg = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (msg == null)
            throw new InvalidOperationException($"Message {messageId} not found");

        msg.Content = content;
        _context.Messages.Update(msg);
        await _context.SaveChangesAsync();
    }
}
