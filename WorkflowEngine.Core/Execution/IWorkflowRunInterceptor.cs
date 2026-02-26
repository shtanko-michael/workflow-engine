using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Execution;

/// <summary>
/// Interceptor for workflow execution events. The engine triggers these methods to notify the external system.
/// Implementations must not throw; the engine wraps all calls in try/catch.
/// </summary>
public interface IWorkflowRunInterceptor
{
    Task OnGraphStartedAsync(WorkflowRunnableConfig config, WorkflowStateBase state, CancellationToken cancellationToken = default);
    Task OnGraphCompletedAsync(WorkflowRunnableConfig config, WorkflowStateBase state, WorkflowGraphCompletionReason reason, CancellationToken cancellationToken = default);
    Task OnSubgraphStartedAsync(string nodeName, WorkflowRunnableConfig config, WorkflowStateBase state, CancellationToken cancellationToken = default);
    Task OnSubgraphCompletedAsync(string nodeName, WorkflowRunnableConfig config, WorkflowStateBase state, CancellationToken cancellationToken = default);
    Task OnNodeStartedAsync(string nodeName, WorkflowRunnableConfig config, WorkflowStateBase state, CancellationToken cancellationToken = default);
    Task OnNodeCompletedAsync(string nodeName, WorkflowRunnableConfig config, WorkflowStateBase state, CancellationToken cancellationToken = default);
    Task OnErrorAsync(WorkflowRunnableConfig config, WorkflowStateBase state, CancellationToken cancellationToken = default);
    Task OnInterruptAsync(WorkflowRunnableConfig config, WorkflowStateBase state, CancellationToken cancellationToken = default);
}