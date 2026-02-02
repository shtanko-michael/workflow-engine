using WorkflowEngine.Tests.UI.Backend.Data.Entities;
using WorkflowEngine.Tests.UI.Backend.Data.Repositories;
using WorkflowEngine.Tests.UI.Backend.Models;
using WorkflowEngine.Tests.UI.Backend.Workflows;

namespace WorkflowEngine.Tests.UI.Backend.Data.Mappers;

public static class DtoMapper
{
    public static DialogDto ToDto(ConversationEntity conversation)
    {
        return new DialogDto(
            conversation.Id,
            conversation.Title ?? "",
            conversation.ThreadId ?? "",
            conversation.WorkflowType ?? DemoChatWorkflow.WorkflowId,
            conversation.LastCheckpointId,
            conversation.LastInterruptRequestId,
            conversation.CreatedAt,
            conversation.UpdatedAt);
    }

    public static MessageWithVersionsDto ToDto(MessageWithAlternatives messageWithAlternatives)
    {
        var active = messageWithAlternatives.ActiveMessage;
        return new MessageWithVersionsDto(
            active.Id,
            active.Role,
            active.Id,
            active.Content,
            messageWithAlternatives.CurrentIndex,
            messageWithAlternatives.TotalAlternatives,
            messageWithAlternatives.Alternatives.Select(m => new MessageVersionDto(
                m.Id,
                m.Id,
                m.Content,
                m.CheckpointId ?? "",
                m.CreatedAt)).ToList(),
            active.CreatedAt);
    }

    /// <summary>Map a single message (e.g. newly created user message) to MessageWithVersionsDto.</summary>
    public static MessageWithVersionsDto FromSingleMessage(MessageEntity m)
    {
        var createdAt = new DateTimeOffset(m.CreatedAt, TimeSpan.Zero);
        return new MessageWithVersionsDto(
            m.Id,
            m.Role,
            m.Id,
            m.Content,
            0,
            1,
            new List<MessageVersionDto> { new MessageVersionDto(m.Id, m.Id, m.Content, m.CheckpointId ?? "", createdAt) },
            createdAt);
    }
}
