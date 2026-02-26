using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Examples.Contracts;

/// <summary>
/// Contract for workflow message operations used in tests and examples.
/// Same shape as AIChat's IWorkflowMessageService; defined here so the engine has no dependency on it.
/// </summary>
public interface IWorkflowMessageService
{
	Task<AIMessage> CreateAssistantMessageAsync(WorkflowRunnableConfig config, string content = "", CancellationToken cancellationToken = default);
	Task StreamChunkAsync(WorkflowRunnableConfig config, string messageId, string chunk, CancellationToken cancellationToken = default);
	Task NotifyStreamEndAsync(WorkflowRunnableConfig config, string messageId, string? fullContent, string[]? options = null, CancellationToken cancellationToken = default);
	Task<AIMessage> CreateErrorMessageAsync(WorkflowRunnableConfig config, string errorType, string? errorDetails, CancellationToken cancellationToken = default);
}
