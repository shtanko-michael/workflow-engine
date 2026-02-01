using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Data.Entities;
using WorkflowEngine.Tests.UI.Backend.Data.Mappers;
using WorkflowEngine.Tests.UI.Backend.Data.Repositories;
using WorkflowEngine.Tests.UI.Backend.Hubs;
using WorkflowEngine.Tests.UI.Backend.Models;
using WorkflowEngine.Tests.UI.Backend.Workflows;

namespace WorkflowEngine.Tests.UI.Backend.Services;

public class ChatWorkflowServiceNew
{
    private readonly WorkflowRegistry _registry;
    private readonly ICheckpointSaver _checkpointer;
    private readonly ILogger<ChatWorkflowServiceNew> _logger;
    private readonly IConversationRepository _conversationRepo;
    private readonly IMessageRepository _messageRepo;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly object _graphLock = new();
    private CompiledWorkflowGraph<DemoChatState>? _graph;

    public ChatWorkflowServiceNew(
        WorkflowRegistry registry,
        ICheckpointSaver checkpointer,
        ILogger<ChatWorkflowServiceNew> logger,
        IConversationRepository conversationRepo,
        IMessageRepository messageRepo,
        IHubContext<ChatHub> hub,
        IServiceScopeFactory scopeFactory)
    {
        _registry = registry;
        _checkpointer = checkpointer;
        _logger = logger;
        _conversationRepo = conversationRepo;
        _messageRepo = messageRepo;
        _hub = hub;
        _scopeFactory = scopeFactory;
    }

    public async Task<ConversationEntity> CreateDialogAsync(string? title)
    {
        var conversation = new ConversationEntity
        {
            Id = Guid.NewGuid().ToString(),
            ThreadId = Guid.NewGuid().ToString(),
            Title = string.IsNullOrWhiteSpace(title) ? "New chat" : title.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _conversationRepo.CreateAsync(conversation);

        var state = await RunWorkflowAsync(conversation.ThreadId!, null, null, null);
        var latestCheckpointId = state.LastCheckpointId;

        var savedMessages = await SaveMessagesFromStateAsync(conversation.Id, state, null, latestCheckpointId, null);
        var lastMessage = savedMessages.LastOrDefault();
        if (lastMessage != null)
        {
            conversation.ActiveLeafMessageId = lastMessage.Id;
            var rootCheckpointNs = lastMessage.Id;
            await _messageRepo.UpdateCheckpointNamespaceAsync(lastMessage.Id, rootCheckpointNs);
            if (!string.IsNullOrWhiteSpace(latestCheckpointId))
            {
                await EnsureCheckpointNamespaceSeedAsync(
                    conversation.ThreadId!,
                    latestCheckpointId,
                    null,
                    rootCheckpointNs);
            }
        }

        conversation.LastInterruptRequestId = state.InterruptRequestId;
        conversation.LastCheckpointId = latestCheckpointId;
        await _conversationRepo.UpdateAsync(conversation);

        await BroadcastDialogUpdatedAsync(conversation);
        await BroadcastMessagesUpdatedAsync(conversation.Id);

        return conversation;
    }

    public async Task<List<ConversationEntity>> GetDialogsAsync()
    {
        return await _conversationRepo.GetAllAsync();
    }

    public async Task<List<MessageEntity>> GetMessagesAsync(string conversationId)
    {
        var conversation = await _conversationRepo.GetByIdAsync(conversationId);
        if (conversation?.ActiveLeafMessageId == null)
            return new List<MessageEntity>();
        return await _messageRepo.GetBranchToLeafAsync(conversationId, conversation.ActiveLeafMessageId);
    }

    public async Task SendMessageAsync(
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
    }

    public async Task<List<MessageEntity>> EditMessageAsync(
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
        await _messageRepo.UpdateActiveLeafAsync(conversationId, newMessage.Id);

        var parentMessage = message.ParentId != null
            ? await _messageRepo.GetMessageAsync(message.ParentId)
            : null;
        var parentCheckpointId = parentMessage?.CheckpointId;
        var parentCheckpointNs = parentMessage?.CheckpointNs;

        var humanMessage = new HumanMessage
        {
            Id = newMessage.Id,
            Content = newContent,
            RequestId = newMessage.RequestId
        };

        var branchCheckpointNs = newMessage.CheckpointNs ?? newMessage.Id;
        if (!string.IsNullOrWhiteSpace(parentCheckpointId))
        {
            await EnsureCheckpointNamespaceSeedAsync(
                conversation.ThreadId!,
                parentCheckpointId,
                parentCheckpointNs,
                branchCheckpointNs);
        }

        var state = await RunWorkflowAsync(conversation.ThreadId!, humanMessage, parentCheckpointId, branchCheckpointNs);
        var latestCheckpointId = state.LastCheckpointId;

        var newMessages = await SaveMessagesFromStateAsync(conversationId, state, newMessage.Id, latestCheckpointId, branchCheckpointNs);
        var lastSaved = newMessages.LastOrDefault();
        if (lastSaved != null)
        {
            conversation.ActiveLeafMessageId = lastSaved.Id;
        }

        conversation.LastCheckpointId = latestCheckpointId;
        conversation.LastInterruptRequestId = state.InterruptRequestId;
        await _conversationRepo.UpdateAsync(conversation);

        await BroadcastDialogUpdatedAsync(conversation);
        await BroadcastMessagesUpdatedAsync(conversationId);

        return newMessages;
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

        await _messageRepo.UpdateActiveLeafAsync(conversationId, leaf.Id);

        var branch = await _messageRepo.GetBranchToLeafAsync(conversationId, leaf.Id);
        var lastInBranch = branch.LastOrDefault();
        if (!string.IsNullOrEmpty(lastInBranch?.CheckpointId))
        {
            conversation.LastCheckpointId = lastInBranch.CheckpointId;
            await _conversationRepo.UpdateAsync(conversation);
        }

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

        var state = await RunWorkflowAsync(conversation.ThreadId!, humanMessage, checkpointId, checkpointNs);
        var latestCheckpointId = state.LastCheckpointId;
        var savedMessages = await SaveMessagesFromStateAsync(conversationId, state, userMessageId, latestCheckpointId, checkpointNs);

        var lastSaved = savedMessages.LastOrDefault();
        if (lastSaved != null)
        {
            conversation.ActiveLeafMessageId = lastSaved.Id;
        }

        conversation.LastInterruptRequestId = state.InterruptRequestId;
        conversation.LastCheckpointId = latestCheckpointId;
        await _conversationRepo.UpdateAsync(conversation);

        await BroadcastDialogUpdatedAsync(conversation);
        await BroadcastMessagesUpdatedAsync(conversationId);
    }

    private async Task<List<MessageEntity>> SaveMessagesFromStateAsync(
        string conversationId,
        DemoChatState state,
        string? parentMessageId,
        string? checkpointId = null,
        string? checkpointNs = null)
    {
        var savedMessages = new List<MessageEntity>();
        var currentParentId = parentMessageId;
        // currently we save only last ai message, but in theory ai can generate multiple messages, so we need to save all of them
        var lastMessage = state.Messages.LastOrDefault();
        var latestMessage = lastMessage != null ? new List<WorkflowMessage> { lastMessage } : [];

        foreach (var stateMessage in latestMessage)
        {
            if (stateMessage is HumanMessage)
                continue;

            var role = stateMessage switch
            {
                AIMessage => "assistant",
                SystemMessage => "system",
                _ => null
            };

            if (role == null)
                continue;

            var content = stateMessage switch
            {
                AIMessage a => a.Content,
                SystemMessage s => s.Content,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(content))
                continue;

            var msg = await _messageRepo.CreateMessageAsync(
                conversationId,
                currentParentId,
                role,
                content,
                checkpointId,
                state.InterruptRequestId,
                checkpointNs);

            savedMessages.Add(msg);
            currentParentId = msg.Id;
        }

        return savedMessages;
    }

    private CompiledWorkflowGraph<DemoChatState> GetGraph()
    {
        if (_graph != null)
            return _graph;

        lock (_graphLock)
        {
            if (_graph != null)
                return _graph;

            var workflowItem = _registry.Get(DemoChatWorkflow.WorkflowId)
                ?? throw new InvalidOperationException("Demo workflow not registered");
            _graph = workflowItem.Compile<DemoChatState>(_checkpointer, _logger);
            return _graph;
        }
    }

    private async Task<DemoChatState> RunWorkflowAsync(
        string threadId,
        HumanMessage? resumeMessage,
        string? checkpointId,
        string? checkpointNs)
    {
        var config = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId
            },
            Context = new WorkflowRunnableContext
            {
                Logger = _logger,
                Tracking = new ClientTrackingContext
                {
                    ThreadId = threadId,
                    WorkflowType = DemoChatWorkflow.WorkflowId
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(checkpointId))
            config.Configurable["checkpoint_id"] = checkpointId;
        if (!string.IsNullOrWhiteSpace(checkpointNs))
            config.Configurable["checkpoint_ns"] = checkpointNs;

        var command = WorkflowCommand<DemoChatState>.Create(resume: resumeMessage);
        var graph = GetGraph();
        return await graph.InvokeAsync(command, config);
    }

    private async Task<string?> GetLatestCheckpointIdAsync(string threadId)
    {
        var config = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId
            }
        };

        var checkpoint = await _checkpointer.GetAsync(config);
        if (checkpoint?.Checkpoint == null)
            return null;

        if (checkpoint.Config.Configurable.TryGetValue("checkpoint_id", out var idValue))
            return idValue?.ToString();

        return checkpoint.Checkpoint.Id;
    }

    private static string? ResolveCheckpointNamespace(MessageEntity message)
    {
        return !string.IsNullOrWhiteSpace(message.CheckpointNs) ? message.CheckpointNs : null;
    }

    private async Task EnsureCheckpointNamespaceSeedAsync(
        string threadId,
        string checkpointId,
        string? fromCheckpointNs,
        string toCheckpointNs)
    {
        var sourceNs = string.IsNullOrWhiteSpace(fromCheckpointNs) ? string.Empty : fromCheckpointNs;
        if (string.Equals(sourceNs, toCheckpointNs, StringComparison.Ordinal))
            return;

        var sourceConfig = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId,
                ["checkpoint_ns"] = sourceNs,
                ["checkpoint_id"] = checkpointId
            }
        };
        var sourceCheckpoint = await _checkpointer.GetAsync(sourceConfig);
        if (sourceCheckpoint?.Checkpoint == null)
            return;

        var targetConfig = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId,
                ["checkpoint_ns"] = toCheckpointNs
            }
        };
        await _checkpointer.PutAsync(
            targetConfig,
            sourceCheckpoint.Checkpoint,
            sourceCheckpoint.Metadata,
            sourceCheckpoint.Checkpoint.ChannelVersions);
    }

    private async Task BroadcastDialogUpdatedAsync(ConversationEntity conversation)
    {
        var payload = DtoMapper.ToDto(conversation);
        await _hub.Clients.Group(conversation.Id).SendAsync("dialogUpdated", payload);
    }

    private async Task BroadcastMessagesUpdatedAsync(string conversationId)
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

        var payload = messagesWithAlternatives.Select(DtoMapper.ToDto).ToList();
        await _hub.Clients.Group(conversationId).SendAsync("messagesUpdated", payload);
    }
}
