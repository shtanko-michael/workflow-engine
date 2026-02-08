using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Graph;

/// <summary>
/// Delegate for workflow node execution
/// </summary>
public delegate Task<WorkflowCommand<TState>> WorkflowNode<TState>(
    TState state,
    WorkflowRunnableContext context,
    Func<Exception, WorkflowCommand<TState>> errorHandler,
    WorkflowRunnableConfig config) where TState : WorkflowStateBase;
