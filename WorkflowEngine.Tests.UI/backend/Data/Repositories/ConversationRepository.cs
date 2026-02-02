using Microsoft.EntityFrameworkCore;
using WorkflowEngine.Tests.UI.Backend.Data.Entities;

namespace WorkflowEngine.Tests.UI.Backend.Data.Repositories;

public class ConversationRepository : IConversationRepository
{
    private readonly ApplicationDbContext _context;

    public ConversationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ConversationEntity?> GetByIdAsync(string id)
    {
        return await _context.Conversations
            .Include(c => c.ActiveLeafMessage)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<List<ConversationEntity>> GetAllAsync()
    {
        return await _context.Conversations
            .Include(c => c.ActiveLeafMessage)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();
    }

    public async Task<ConversationEntity> CreateAsync(ConversationEntity conversation)
    {
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();
        return conversation;
    }

    public async Task UpdateAsync(ConversationEntity conversation)
    {
        conversation.UpdatedAt = DateTime.UtcNow;
        _context.Conversations.Update(conversation);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        var conversation = await _context.Conversations.FindAsync(id);
        if (conversation == null)
            return;

        // Break circular FK: Conversation.ActiveLeafMessageId -> Message.ConversationId -> Conversation
        conversation.ActiveLeafMessageId = null;
        await _context.SaveChangesAsync();

        _context.Conversations.Remove(conversation);
        await _context.SaveChangesAsync();
    }
}
