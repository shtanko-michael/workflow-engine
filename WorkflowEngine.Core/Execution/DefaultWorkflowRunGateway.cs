using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Execution;

/// <summary>
/// Default gateway when no external bridge is provided: in-memory message creation and optional legacy chunk callback (chunk only, no messageId).
/// </summary>
public sealed class DefaultWorkflowRunGateway : IWorkflowRunGateway
{
    private readonly Func<string, Task>? _legacyChunkCallback;

    public DefaultWorkflowRunGateway(Func<string, Task>? legacyChunkCallback = null)
    {
        _legacyChunkCallback = legacyChunkCallback;
    }

    public Task<AIMessage> CreateAssistantMessageAsync(WorkflowRunnableConfig config, string content = "", CancellationToken cancellationToken = default)
    {
        var message = new AIMessage
        {
            Id = Guid.NewGuid().ToString(),
            Content = content
        };
        return Task.FromResult(message);
    }

    public Task StreamChunkAsync(WorkflowRunnableConfig config, string messageId, string chunk, CancellationToken cancellationToken = default)
    {
        if (_legacyChunkCallback != null)
            return _legacyChunkCallback(chunk);
        return Task.CompletedTask;
    }

    public Task NotifyStreamEndAsync(WorkflowRunnableConfig config, string messageId, string? fullContent = null, string[]? options = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
