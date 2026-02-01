using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Hubs;
using WorkflowEngine.Tests.UI.Backend.Models;
using WorkflowEngine.Tests.UI.Backend.Workflows;

namespace WorkflowEngine.Tests.UI.Backend.Services;

public class ChatWorkflowService
{
    private readonly WorkflowRegistry _registry;
    private readonly ICheckpointSaver _checkpointer;
    private readonly ILogger<ChatWorkflowService> _logger;
    private readonly InMemoryChatStore _store;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly object _graphLock = new();
    private CompiledWorkflowGraph<DemoChatState>? _graph;

    public ChatWorkflowService(
        WorkflowRegistry registry,
        ICheckpointSaver checkpointer,
        ILogger<ChatWorkflowService> logger,
        InMemoryChatStore store,
        IHubContext<ChatHub> hub,
        IServiceScopeFactory scopeFactory)
    {
        _registry = registry;
        _checkpointer = checkpointer;
        _logger = logger;
        _store = store;
        _hub = hub;
        _scopeFactory = scopeFactory;
    }

    public async Task<Dialog> CreateDialogAsync(string? title)
    {
        var dialog = new Dialog
        {
            Id = Guid.NewGuid().ToString(),
            ThreadId = Guid.NewGuid().ToString(),
            WorkflowType = DemoChatWorkflow.WorkflowId,
            Title = string.IsNullOrWhiteSpace(title) ? "New dialog" : title.Trim()
        };

        _store.AddDialog(dialog);

        var state = await RunWorkflowAsync(dialog, null, null);
        var addedMessages = SyncMessages(dialog, state);
        await UpdateDialogCheckpointAsync(dialog, state);
        await BroadcastDialogUpdatedAsync(dialog);
        await BroadcastMessagesAsync(dialog.Id, addedMessages);

        return dialog;
    }

    public IReadOnlyCollection<Dialog> GetDialogs()
    {
        return _store.GetDialogs();
    }

    public IReadOnlyCollection<ChatMessage> GetMessages(string dialogId)
    {
        return _store.GetMessages(dialogId);
    }

    public Task<IReadOnlyCollection<ChatMessage>> SendMessageAsync(
        string dialogId,
        string content,
        string threadId,
        string checkpointId,
        string? requestId)
    {
        var dialog = _store.GetDialog(dialogId);
        if (dialog == null)
            throw new InvalidOperationException("Dialog not found");

        if (!string.Equals(dialog.ThreadId, threadId, StringComparison.Ordinal))
            throw new InvalidOperationException("Thread mismatch");

        if (!string.Equals(dialog.LastCheckpointId, checkpointId, StringComparison.Ordinal))
            throw new InvalidOperationException("Checkpoint mismatch");

        var effectiveRequestId = string.IsNullOrWhiteSpace(requestId)
            ? dialog.LastInterruptRequestId
            : requestId;
        var humanMessage = new HumanMessage
        {
            Id = Guid.NewGuid().ToString(),
            Content = content,
            RequestId = effectiveRequestId
        };

        var userMessage = new ChatMessage
        {
            Id = humanMessage.Id,
            DialogId = dialogId,
            Role = "user",
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
            RequestId = effectiveRequestId
        };

        _store.AddMessages(dialogId, new[]
        {
            userMessage
        });

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<ChatWorkflowService>();
                await scopedService.ProcessWorkflowAsync(dialogId, humanMessage, checkpointId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process workflow for dialog {DialogId}", dialogId);
            }
        });

        return Task.FromResult<IReadOnlyCollection<ChatMessage>>(new[] { userMessage });
    }

    private async Task ProcessWorkflowAsync(string dialogId, HumanMessage humanMessage, string checkpointId)
    {
        var dialog = _store.GetDialog(dialogId);
        if (dialog == null)
        {
            _logger.LogWarning("Dialog {DialogId} not found for workflow processing", dialogId);
            return;
        }

        var state = await RunWorkflowAsync(dialog, humanMessage, checkpointId);
        var addedMessages = SyncMessages(dialog, state);
        await UpdateDialogCheckpointAsync(dialog, state);
        await BroadcastDialogUpdatedAsync(dialog);
        await BroadcastMessagesAsync(dialog.Id, addedMessages);
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
        Dialog dialog,
        HumanMessage? resumeMessage,
        string? checkpointId)
    {
        var config = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = dialog.ThreadId
            },
            Context = new WorkflowRunnableContext
            {
                Logger = _logger,
                Tracking = new ClientTrackingContext
                {
                    ThreadId = dialog.ThreadId,
                    WorkflowType = dialog.WorkflowType
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(checkpointId))
            config.Configurable["checkpoint_id"] = checkpointId;

        var command = WorkflowCommand<DemoChatState>.Create(
            resume: resumeMessage);

        var graph = GetGraph();
        return await graph.InvokeAsync(command, config);
    }

    private IReadOnlyCollection<ChatMessage> SyncMessages(Dialog dialog, DemoChatState state)
    {
        var mapped = new List<ChatMessage>();

        foreach (var message in state.Messages)
        {
            var payload = message switch
            {
                HumanMessage human => new { Role = "user", Content = human.Content, RequestId = human.RequestId },
                AIMessage ai => new { Role = "assistant", Content = ai.Content, RequestId = (string?)null },
                SystemMessage system => new { Role = "system", Content = system.Content, RequestId = (string?)null },
                _ => null
            };

            if (payload == null || string.IsNullOrWhiteSpace(payload.Content))
                continue;

            mapped.Add(new ChatMessage
            {
                Id = message.Id,
                DialogId = dialog.Id,
                Role = payload.Role,
                Content = payload.Content!,
                CreatedAt = DateTimeOffset.UtcNow,
                RequestId = payload.RequestId
            });
        }

        return _store.AddMessages(dialog.Id, mapped);
    }

    private async Task UpdateDialogCheckpointAsync(Dialog dialog, DemoChatState state)
    {
        dialog.LastInterruptRequestId = state.InterruptRequestId;
        dialog.LastCheckpointId = await GetLatestCheckpointIdAsync(dialog.ThreadId);
        _store.UpdateDialog(dialog);
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

    private Task BroadcastDialogUpdatedAsync(Dialog dialog)
    {
        var payload = ToDto(dialog);
        return _hub.Clients.Group(dialog.Id).SendAsync("dialogUpdated", payload);
    }

    private Task BroadcastMessagesAsync(string dialogId, IReadOnlyCollection<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return Task.CompletedTask;

        var payload = messages.Select(ToDto).ToList();
        return _hub.Clients.Group(dialogId).SendAsync("messagesAdded", payload);
    }

    public static DialogDto ToDto(Dialog dialog)
    {
        return new DialogDto(
            dialog.Id,
            dialog.Title,
            dialog.ThreadId,
            dialog.WorkflowType,
            dialog.LastCheckpointId,
            dialog.LastInterruptRequestId,
            dialog.CreatedAt,
            dialog.UpdatedAt);
    }

    public static MessageDto ToDto(ChatMessage message)
    {
        return new MessageDto(
            message.Id,
            message.DialogId,
            message.Role,
            message.Content,
            message.CreatedAt,
            message.RequestId);
    }
}
