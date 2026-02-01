using Microsoft.Extensions.Logging;
using System.Text.Json;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
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
        return WithContextNode.Wrap<TState>("errorHandler", (state, ctx, errorHandler, config) =>
        {
            // Log error
            ctx.Logger.LogError("Error in workflow: {ErrorName}, Message: {ErrorMessage}", 
                state.ErrorName, state.ErrorMessage);
            
            // Clear error and return to askHuman
            // Copy state and clear error fields
            var stateJson = JsonSerializer.Serialize(state);
            var updatedState = JsonSerializer.Deserialize<TState>(stateJson);
            if (updatedState is WorkflowStateBase baseState)
            {
                baseState.ErrorMessage = null;
                baseState.ErrorName = null;
                // Keep interruptCaller to know where to return
            }
            
            return Task.FromResult(WorkflowCommand<TState>.Create(
                gotoNode: "askHuman",
                update: updatedState
            ));
        });
    }
}
