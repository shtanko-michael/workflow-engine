using Microsoft.AspNetCore.SignalR;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Data.Entities;
using WorkflowEngine.Tests.UI.Backend.Data.Mappers;
using WorkflowEngine.Tests.UI.Backend.Data.Repositories;
using WorkflowEngine.Tests.UI.Backend.Hubs;
using WorkflowEngine.Tests.UI.Backend.Models;
using WorkflowEngine.Tests.UI.Backend.Workflows;
using WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;
using WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

namespace WorkflowEngine.Tests.UI.Backend.Services;

public class ChatWorkflowServiceNew
{
    private readonly WorkflowController _workflowController;
    private readonly ILogger<ChatWorkflowServiceNew> _logger;
    private readonly IConversationRepository _conversationRepo;
    private readonly IMessageRepository _messageRepo;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICheckpointSaverFactory _checkpointer;

    public ChatWorkflowServiceNew(
        WorkflowController workflowController,
        ILogger<ChatWorkflowServiceNew> logger,
        IConversationRepository conversationRepo,
        IMessageRepository messageRepo,
        IHubContext<ChatHub> hub,
        IServiceScopeFactory scopeFactory,
        ICheckpointSaverFactory checkpointer)
    {
        _workflowController = workflowController;
        _logger = logger;
        _conversationRepo = conversationRepo;
        _messageRepo = messageRepo;
        _hub = hub;
        _scopeFactory = scopeFactory;
        _checkpointer = checkpointer;
    }

    public async Task<ConversationEntity> CreateDialogAsync(string workflowId, string? title)
    {
        var conversation = new ConversationEntity
        {
            Id = Guid.NewGuid().ToString(),
            ThreadId = Guid.NewGuid().ToString(),
            Title = string.IsNullOrWhiteSpace(title) ? "New chat" : title.Trim(),
            WorkflowType = workflowId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _conversationRepo.CreateAsync(conversation);

        var conversationId = conversation.Id;
        var threadId = conversation.ThreadId!;
        var workflowType = conversation.WorkflowType!;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<ChatWorkflowServiceNew>();
                await scopedService.RunWorkflowAfterCreateAsync(conversationId, threadId, workflowType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run workflow after create for conversation {ConversationId}", conversationId);
            }
        });

        return conversation;
    }

    public async Task<List<ConversationEntity>> GetDialogsAsync()
    {
        return await _conversationRepo.GetAllAsync();
    }

    public async Task DeleteDialogAsync(string conversationId)
    {
        var conversation = await _conversationRepo.GetByIdAsync(conversationId);
        if (conversation == null)
            throw new InvalidOperationException("Conversation not found.");
        await _conversationRepo.DeleteAsync(conversationId);
    }

    public async Task<List<MessageEntity>> GetMessagesAsync(string conversationId)
    {
        var conversation = await _conversationRepo.GetByIdAsync(conversationId);
        if (conversation?.ActiveLeafMessageId == null)
            return new List<MessageEntity>();
        return await _messageRepo.GetBranchToLeafAsync(conversationId, conversation.ActiveLeafMessageId);
    }

    public async Task<MessageEntity> SendMessageAsync(
        string conversationId,
        string content,
        string checkpointId)
    {
        var conversation = await _conversationRepo.GetByIdAsync(conversationId);
        if (conversation == null)
            throw new InvalidOperationException("Conversation not found");

        if (!string.Equals(conversation.LastCheckpointId, checkpointId, StringComparison.Ordinal))
            throw new InvalidOperationException("Checkpoint mismatch");

        var branch = await _messageRepo.GetBranchToLeafAsync(conversationId, conversation.ActiveLeafMessageId);
        var lastMessage = branch.LastOrDefault();
        if (lastMessage == null)
            throw new InvalidOperationException("No active branch found");

        var checkpointNs = ResolveCheckpointNamespace(lastMessage);
        var userMessage = await _messageRepo.CreateMessageAsync(
            conversationId,
            lastMessage.Id,
            "user",
            content,
            checkpointId,
            conversation.LastInterruptRequestId,
            checkpointNs);

        var humanMessage = new HumanMessage
        {
            Id = userMessage.Id,
            Content = content,
            RequestId = conversation.LastInterruptRequestId
        };

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<ChatWorkflowServiceNew>();
                await scopedService.ProcessWorkflowAsync(conversationId, humanMessage, checkpointId, checkpointNs, userMessage.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process workflow for conversation {ConversationId}", conversationId);
            }
        });

        return userMessage;
    }

    /// <summary>
    /// Edits a user message: creates a new sibling with new content, switches active branch to it,
    /// returns the new branch (so UI can show edited message and clear messages below), and runs the workflow in background with streaming.
    /// </summary>
    public async Task<List<MessageWithVersionsDto>> EditMessageAsync(
        string conversationId,
        string messageId,
        string newContent)
    {
        var message = await _messageRepo.GetMessageAsync(messageId);
        if (message == null || message.Role != "user")
            throw new InvalidOperationException("Can only edit user messages");

        var conversation = await _conversationRepo.GetByIdAsync(conversationId);
        if (conversation == null)
            throw new InvalidOperationException("Conversation not found");

        var newMessage = await _messageRepo.CreateSiblingAsync(messageId, newContent);
        // await _messageRepo.UpdateActiveLeafAsync(conversationId, newMessage.Id);

        conversation.ActiveLeafMessageId = newMessage.Id;
        await _conversationRepo.UpdateAsync(conversation);

        var parentMessage = message.ParentId != null
            ? await _messageRepo.GetMessageAsync(message.ParentId)
            : null;
        var parentCheckpointId = parentMessage?.CheckpointId;
        var parentCheckpointNs = parentMessage?.CheckpointNs;

        var branchCheckpointNs = newMessage.CheckpointNs ?? newMessage.Id;
        // if (!string.IsNullOrWhiteSpace(parentCheckpointId))
        // {
        //     await EnsureCheckpointNamespaceSeedAsync(
        //         conversation.ThreadId!,
        //         parentCheckpointId,
        //         parentCheckpointNs,
        //         branchCheckpointNs);
        // }

        var branchWithAlternatives = await GetBranchWithAlternativesAsync(conversationId);
        var dtos = branchWithAlternatives.Select(DtoMapper.ToDto).ToList();

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<ChatWorkflowServiceNew>();
                await scopedService.ProcessWorkflowAfterEditAsync(
                    conversationId,
                    newMessage.Id,
                    newContent,
                    newMessage.RequestId,
                    parentCheckpointId,
                    branchCheckpointNs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process workflow after edit for conversation {ConversationId}", conversationId);
            }
        });

        return dtos;
    }

    private async Task ProcessWorkflowAfterEditAsync(
        string conversationId,
        string newMessageId,
        string newContent,
        string? requestId,
        string? parentCheckpointId,
        string? branchCheckpointNs)
    {
        var conversation = await _conversationRepo.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversation {ConversationId} not found for workflow processing after edit", conversationId);
            return;
        }

        var humanMessage = new HumanMessage
        {
            Id = newMessageId,
            Content = newContent,
            RequestId = requestId
        };

        var state = await RunWorkflowAsync(conversation.ThreadId!, conversation.WorkflowType!, humanMessage, parentCheckpointId, branchCheckpointNs, conversationId, requestId);
        var latestCheckpointId = state.LastCheckpointId;
        // var lastMessageId = GetLastMessageIdFromState(state);
        // if (!string.IsNullOrWhiteSpace(lastMessageId))
        // {
        //     conversation.ActiveLeafMessageId = lastMessageId;
        // }

        conversation.LastInterruptRequestId = state.InterruptRequestId;
        conversation.LastCheckpointId = latestCheckpointId;
        await _conversationRepo.UpdateAsync(conversation);

        await BroadcastDialogUpdatedAsync(conversation);
        await BroadcastMessagesUpdatedAsync(conversationId);
    }

    public async Task SwitchVersionAsync(string conversationId, string messageId)
    {
        var message = await _messageRepo.GetMessageAsync(messageId);
        if (message == null)
            throw new InvalidOperationException("Message not found");

        var conversation = await _conversationRepo.GetByIdAsync(conversationId);
        if (conversation == null)
            throw new InvalidOperationException("Conversation not found");

        var leaf = await _messageRepo.GetLeafOfBranchContainingAsync(conversationId, messageId);
        if (leaf == null)
            throw new InvalidOperationException("Failed to find leaf of branch");

        conversation.ActiveLeafMessageId = leaf.Id;
        var branch = await _messageRepo.GetBranchToLeafAsync(conversationId, leaf.Id);
        var lastInBranch = branch.LastOrDefault();
        if (!string.IsNullOrEmpty(lastInBranch?.CheckpointId))
        {
            conversation.LastCheckpointId = lastInBranch.CheckpointId;
        }
        await _conversationRepo.UpdateAsync(conversation);

        await BroadcastDialogUpdatedAsync(conversation);
        await BroadcastMessagesUpdatedAsync(conversationId);
    }

    /// <summary>
    /// Runs workflow after dialog create (fire-and-forget). Saves tail AI messages, updates conversation, broadcasts.
    /// </summary>
    public async Task RunWorkflowAfterCreateAsync(string conversationId, string threadId, string workflowId)
    {
        // Brief delay so the client can join the SignalR group (JoinDialog) before first chunks.
        await Task.Delay(500).ConfigureAwait(false);

        var state = await RunWorkflowAsync(threadId, workflowId, null, null, null, conversationId, null);
        var conversation = await _conversationRepo.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversation {ConversationId} not found after workflow run", conversationId);
            return;
        }

        // var latestCheckpointId = state.LastCheckpointId;
        // var lastMessageId = GetLastMessageIdFromState(state);
        // if (!string.IsNullOrWhiteSpace(lastMessageId))
        // {
        //     conversation.ActiveLeafMessageId = lastMessageId;
        //     // if (!string.IsNullOrWhiteSpace(latestCheckpointId))
        //     // {
        //     //     await EnsureCheckpointNamespaceSeedAsync(threadId, latestCheckpointId, null, lastMessageId);
        //     // }
        // }

        conversation.LastInterruptRequestId = state.InterruptRequestId;
        conversation.LastCheckpointId = state.LastCheckpointId;
        await _conversationRepo.UpdateAsync(conversation);

        await BroadcastDialogUpdatedAsync(conversation);
        await BroadcastMessagesUpdatedAsync(conversationId);
    }

    private async Task ProcessWorkflowAsync(
        string conversationId,
        HumanMessage humanMessage,
        string checkpointId,
        string? checkpointNs,
        string userMessageId)
    {
        var conversation = await _conversationRepo.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversation {ConversationId} not found for workflow processing", conversationId);
            return;
        }

        var state = await RunWorkflowAsync(conversation.ThreadId!, conversation.WorkflowType!, humanMessage, checkpointId, checkpointNs, conversationId, conversation.LastInterruptRequestId);
        var latestCheckpointId = state.LastCheckpointId;
        // var lastMessageId = GetLastMessageIdFromState(state);
        // if (!string.IsNullOrWhiteSpace(lastMessageId))
        // {
        //     conversation.ActiveLeafMessageId = lastMessageId;
        // }

        conversation.LastInterruptRequestId = state.InterruptRequestId;
        conversation.LastCheckpointId = latestCheckpointId;
        await _conversationRepo.UpdateAsync(conversation);

        await BroadcastDialogUpdatedAsync(conversation);
        await BroadcastMessagesUpdatedAsync(conversationId);
    }

    /// <summary>
    /// Returns the Id of the last message in state (the new leaf). All assistant messages are already created in DB via the bridge.
    /// </summary>
    // private static string? GetLastMessageIdFromState(WorkflowStateBase state)
    // {
    //     var last = state.Messages.LastOrDefault();
    //     return last?.Id;
    // }

    private async Task<WorkflowStateBase> RunWorkflowAsync(
        string threadId,
        string workflowId,
        HumanMessage? resumeMessage,
        string? checkpointId,
        string? checkpointNs,
        string? conversationId = null,
        string? interruptRequestId = null)
    {
        IWorkflowRunGateway? gateway = null;
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            gateway = new ChatWorkflowBridge(
                conversationId,
                _messageRepo,
                _conversationRepo,
                _hub);
        }

        var mainNs = checkpointNs?.Split(":")[0];
        var executeConfig = new WorkflowControllerExecuteConfig
        {
            WorkflowType = workflowId,
            ThreadId = threadId,
            ResumeMessage = resumeMessage,
            CheckpointId = checkpointId,
            CheckpointNs = mainNs,
            Gateway = gateway
        };

        return workflowId switch
        {
            DemoChatWorkflow.WorkflowId => await _workflowController.ExecuteAsync<DemoChatState>(executeConfig),
            AIChatWorkflow.WorkflowId => await _workflowController.ExecuteAsync<AIChatState>(executeConfig),
            OnboardingConstants.WorkflowId => await _workflowController.ExecuteAsync<OnboardingState>(executeConfig),
            RoutedChatConstants.WorkflowId => await _workflowController.ExecuteAsync<RoutedChatState>(executeConfig),
            _ => throw new InvalidOperationException($"Unsupported workflow type '{workflowId}'")
        };
    }

    private static string? ResolveCheckpointNamespace(MessageEntity message)
    {
        return !string.IsNullOrWhiteSpace(message.CheckpointNs) ? message.CheckpointNs : null;
    }

    // private async Task EnsureCheckpointNamespaceSeedAsync(
    //     string threadId,
    //     string checkpointId,
    //     string? fromCheckpointNs,
    //     string toCheckpointNs)
    // {
    //     var sourceNs = string.IsNullOrWhiteSpace(fromCheckpointNs) ? string.Empty : fromCheckpointNs;
    //     if (string.Equals(sourceNs, toCheckpointNs, StringComparison.Ordinal))
    //         return;

    //     var sourceConfig = new WorkflowRunnableConfig
    //     {
    //         Configurable = new Dictionary<string, object>
    //         {
    //             ["thread_id"] = threadId,
    //             ["checkpoint_ns"] = sourceNs,
    //             ["checkpoint_id"] = checkpointId
    //         }
    //     };
    //     var c = await _checkpointer.Build();
    //     var sourceCheckpoint = await c.GetAsync(sourceConfig);
    //     if (sourceCheckpoint?.Checkpoint == null)
    //         return;

    //     var targetConfig = new WorkflowRunnableConfig
    //     {
    //         Configurable = new Dictionary<string, object>
    //         {
    //             ["thread_id"] = threadId,
    //             ["checkpoint_ns"] = toCheckpointNs
    //         }
    //     };
    //     await c.PutAsync(
    //         targetConfig,
    //         sourceCheckpoint.Checkpoint,
    //         sourceCheckpoint.Metadata,
    //         sourceCheckpoint.Checkpoint.ChannelVersions);
    // }

    private async Task BroadcastDialogUpdatedAsync(ConversationEntity conversation)
    {
        var payload = DtoMapper.ToDto(conversation);
        await _hub.Clients.Group(conversation.Id).SendAsync("dialogUpdated", payload);
    }

    private async Task<List<MessageWithAlternatives>> GetBranchWithAlternativesAsync(string conversationId)
    {
        var branch = await GetMessagesAsync(conversationId);
        var messagesWithAlternatives = new List<MessageWithAlternatives>();

        for (var i = 0; i < branch.Count; i++)
        {
            var msg = branch[i];
            var alternatives = i == 0
                ? new List<MessageEntity>()
                : await _messageRepo.GetChildrenAsync(branch[i - 1].Id);
            var sorted = alternatives.OrderBy(m => m.CreatedAt).ToList();
            var currentIndex = sorted.FindIndex(m => m.Id == msg.Id);

            messagesWithAlternatives.Add(new MessageWithAlternatives
            {
                ActiveMessage = msg,
                Alternatives = sorted,
                CurrentIndex = currentIndex >= 0 ? currentIndex : 0,
                TotalAlternatives = sorted.Count
            });
        }

        return messagesWithAlternatives;
    }

    private async Task BroadcastMessagesUpdatedAsync(string conversationId)
    {
        var messagesWithAlternatives = await GetBranchWithAlternativesAsync(conversationId);
        var payload = messagesWithAlternatives.Select(DtoMapper.ToDto).ToList();
        await _hub.Clients.Group(conversationId).SendAsync("messagesUpdated", payload);
    }
}
