using Microsoft.AspNetCore.SignalR;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Data.Repositories;
using WorkflowEngine.Tests.UI.Backend.Hubs;

namespace WorkflowEngine.Tests.UI.Backend.Services;

/// <summary>
/// Gateway implementation that creates assistant messages in the database and streams chunks via SignalR by message Id.
/// Uses config (when set) for current checkpoint_id and checkpoint_ns so messages are created with correct checkpoint metadata.
/// </summary>
public sealed class ChatWorkflowBridge : IWorkflowRunGateway
{
    private readonly string _conversationId;
    private readonly IMessageRepository _messageRepo;
    private readonly IConversationRepository _convRepo;
    private readonly IHubContext<ChatHub> _hub;

    public ChatWorkflowBridge(
        string conversationId,
        IMessageRepository messageRepo,
        IConversationRepository convRepo,
        IHubContext<ChatHub> hub)
    {
        _conversationId = conversationId ?? throw new ArgumentNullException(nameof(conversationId));
        _messageRepo = messageRepo ?? throw new ArgumentNullException(nameof(messageRepo));
        _convRepo = convRepo ?? throw new ArgumentNullException(nameof(convRepo));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    }

    public async Task<AIMessage> CreateAssistantMessageAsync(WorkflowRunnableConfig config, string? parentMessageId = null, string content = "", CancellationToken cancellationToken = default)
    {
        var entity = await _messageRepo.CreateMessageAsync(
            _conversationId,
            parentMessageId,
            "assistant",
            content,
            config.CheckpointId,
            config.LastMessageId,
            config.CheckpointNs);

        await _convRepo.SetActiveLeafId(_conversationId, entity.Id);

        return new AIMessage
        {
            Id = entity.Id,
            Content = string.Empty
        };
    }

    public Task StreamChunkAsync(WorkflowRunnableConfig config, string messageId, string chunk, CancellationToken cancellationToken = default)
    {
        return _hub.Clients.Group(_conversationId).SendAsync("assistantChunk", _conversationId, messageId, chunk, cancellationToken);
    }

    public async Task NotifyStreamEndAsync(WorkflowRunnableConfig config, string messageId, string? fullContent = null, CancellationToken cancellationToken = default)
    {
        await _messageRepo.UpdateContentAsync(messageId, fullContent ?? string.Empty);
    }
}
