using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Exceptions;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Nodes;

/// <summary>
/// Node for handling errors in workflow
/// </summary>
public static class ErrorHandlerNode
{
    /// <summary>
    /// Creates an errorHandler node
    /// </summary>
    public static WorkflowNode<TState> Create<TState>() where TState : WorkflowStateBase
    {
        return WithContextNode.Wrap<TState>(WorkflowEdges.ErrorHandler, async (state, ctx, errorHandler, config) =>
        {
            ctx.Logger.LogError("Error in workflow: {ErrorName}, Message: {ErrorMessage}",
                state.ErrorName, state.ErrorMessage);

            var lastMessage = state.Messages.LastOrDefault();
            throw new WorkflowInterruptErrorException(lastMessage?.Id ?? "", state.InterruptCaller ?? WorkflowEdges.End);
        });
    }
}
