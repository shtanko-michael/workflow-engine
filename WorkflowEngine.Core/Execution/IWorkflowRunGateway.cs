using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Execution;

/// <summary>
/// Single entry point for nodes to create assistant messages and stream content.
/// Provided by the controller via context; implementation may delegate to an external system (bridge) or use in-memory/default behavior.
/// </summary>
public interface IWorkflowRunGateway
{
    /// <summary>
    /// Creates an assistant message slot. When a bridge is used, this creates the message in the external system and returns it with Id set.
    /// Otherwise returns an in-memory message with a new Id.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AIMessage> CreateAssistantMessageAsync(WorkflowRunnableConfig config, string content = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a chunk to the message identified by <paramref name="messageId"/> (e.g. push to clients via SignalR).
    /// No-op when no bridge/streaming is configured.
    /// </summary>
    Task StreamChunkAsync(WorkflowRunnableConfig config, string messageId, string chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies that streaming for this message is finished. Optional finalization (e.g. update content in DB).
    /// When options are provided, they are persisted and can be sent to clients for rendering quick-reply choices.
    /// </summary>
    Task NotifyStreamEndAsync(WorkflowRunnableConfig config, string messageId, string? fullContent, string[]? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an error message in the external system (when bridge is used) and updates dialog state to error.
    /// Otherwise returns an in-memory AIMessage with a new Id.
    /// </summary>
    Task<AIMessage> CreateErrorMessageAsync(WorkflowRunnableConfig config, string errorType, string? errorDetails, CancellationToken cancellationToken = default);
}
