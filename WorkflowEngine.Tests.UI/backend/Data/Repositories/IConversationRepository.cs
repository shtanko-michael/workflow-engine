using WorkflowEngine.Tests.UI.Backend.Data.Entities;

namespace WorkflowEngine.Tests.UI.Backend.Data.Repositories;

public interface IConversationRepository
{
    Task<ConversationEntity?> GetByIdAsync(string id);
    Task<List<ConversationEntity>> GetAllAsync();
    Task<ConversationEntity> CreateAsync(ConversationEntity conversation);
    Task UpdateAsync(ConversationEntity conversation);
    Task DeleteAsync(string id);
}
