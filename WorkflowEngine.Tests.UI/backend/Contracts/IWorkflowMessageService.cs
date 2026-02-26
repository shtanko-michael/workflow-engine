using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Contracts;

/// <summary>
/// Contract for creating and streaming assistant/error messages during workflow execution in Tests.UI.
/// Same shape as Core's former IWorkflowMessageService; defined here so the engine has no dependency on it.
/// </summary>
public interface IWorkflowMessageService
{
	Task<AIMessage> CreateAssistantMessageAsync(WorkflowRunnableConfig config, string content = "", CancellationToken cancellationToken = default);
	Task StreamChunkAsync(WorkflowRunnableConfig config, string messageId, string chunk, CancellationToken cancellationToken = default);
	Task NotifyStreamEndAsync(WorkflowRunnableConfig config, string messageId, string? fullContent, string[]? options = null, CancellationToken cancellationToken = default);
	Task<AIMessage> CreateErrorMessageAsync(WorkflowRunnableConfig config, string errorType, string? errorDetails, CancellationToken cancellationToken = default);
}
