using Microsoft.AspNetCore.SignalR;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Contracts;
using WorkflowEngine.Tests.UI.Backend.Data.Repositories;
using WorkflowEngine.Tests.UI.Backend.Hubs;

namespace WorkflowEngine.Tests.UI.Backend.Services;

/// <summary>
/// Message service that creates assistant messages in the database and streams via SignalR.
/// Uses IWorkflowRunScope for the current conversation id.
/// </summary>
public sealed class ChatWorkflowMessageService : IWorkflowMessageService
{
	private readonly IWorkflowRunScope _runScope;
	private readonly IMessageRepository _messageRepo;
	private readonly IConversationRepository _convRepo;
	private readonly IHubContext<ChatHub> _hub;

	public ChatWorkflowMessageService(
		IWorkflowRunScope runScope,
		IMessageRepository messageRepo,
		IConversationRepository convRepo,
		IHubContext<ChatHub> hub)
	{
		_runScope = runScope ?? throw new ArgumentNullException(nameof(runScope));
		_messageRepo = messageRepo ?? throw new ArgumentNullException(nameof(messageRepo));
		_convRepo = convRepo ?? throw new ArgumentNullException(nameof(convRepo));
		_hub = hub ?? throw new ArgumentNullException(nameof(hub));
	}

	public async Task<AIMessage> CreateAssistantMessageAsync(WorkflowRunnableConfig config, string content = "", CancellationToken cancellationToken = default)
	{
		var conversationId = _runScope.ConversationId ?? throw new InvalidOperationException("Workflow run scope ConversationId is not set");
		var conv = await _convRepo.GetByIdAsync(conversationId);
		var entity = await _messageRepo.CreateMessageAsync(
			conversationId,
			conv.ActiveLeafMessageId,
			"assistant",
			content,
			config.CheckpointId,
			config.LastMessageId,
			config.CheckpointNs);

		conv.ActiveLeafMessageId = entity.Id;
		await _convRepo.UpdateAsync(conv);

		return new AIMessage { Id = entity.Id, Content = content };
	}

	public Task StreamChunkAsync(WorkflowRunnableConfig config, string messageId, string chunk, CancellationToken cancellationToken = default)
	{
		var conversationId = _runScope.ConversationId ?? throw new InvalidOperationException("Workflow run scope ConversationId is not set");
		return _hub.Clients.Group(conversationId).SendAsync("assistantChunk", conversationId, messageId, chunk, cancellationToken);
	}

	public async Task NotifyStreamEndAsync(WorkflowRunnableConfig config, string messageId, string? fullContent = null, string[]? options = null, CancellationToken cancellationToken = default)
	{
		var conversationId = _runScope.ConversationId ?? throw new InvalidOperationException("Workflow run scope ConversationId is not set");
		await _messageRepo.UpdateContentAsync(messageId, fullContent ?? string.Empty);
		if (options != null && options.Length > 0)
		{
			await _messageRepo.UpdateOptionsAsync(messageId, options);
			await _hub.Clients.Group(conversationId).SendAsync("assistantOptions", conversationId, messageId, options, cancellationToken);
		}
	}

	public async Task<AIMessage> CreateErrorMessageAsync(WorkflowRunnableConfig config, string errorType, string? errorDetails, CancellationToken cancellationToken = default)
	{
		var conversationId = _runScope.ConversationId ?? throw new InvalidOperationException("Workflow run scope ConversationId is not set");
		var conv = await _convRepo.GetByIdAsync(conversationId);
		var content = $"[{errorType}] {errorDetails ?? "Unknown error"}";
		var entity = await _messageRepo.CreateMessageAsync(
			conversationId,
			conv.ActiveLeafMessageId,
			"assistant",
			content,
			config.CheckpointId,
			config.LastMessageId,
			config.CheckpointNs);

		conv.ActiveLeafMessageId = entity.Id;
		await _convRepo.UpdateAsync(conv);

		return new AIMessage { Id = entity.Id, Content = content };
	}
}
