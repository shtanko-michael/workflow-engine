using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Exceptions;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Nodes;

/// <summary>
/// Node that interrupts workflow to ask for human input
/// </summary>
public static class AskHumanNode
{
    /// <summary>
    /// Creates an askHuman node
    /// </summary>
    public static WorkflowNode<TState> Create<TState>() where TState : WorkflowStateBase
    {
        return WithContextNode.Wrap<TState>("askHuman", (state, ctx, errorHandler, config) =>
        {
            var hasCommand = config.Configurable.TryGetValue(WorkflowGlobals.WorkflowCommandKey, out var command);
            if (hasCommand
                && command is WorkflowCommand<TState> workflowCommand
                && workflowCommand.IsResume
            )
            {
                var returnNode = state.InterruptCaller ?? WorkflowEdges.End;
                state.InterruptRequestId = null;
                state.InterruptCaller = null;
                state.InterruptReason = null;
                if (workflowCommand.Resume is HumanMessage msg)
                    state.Messages.Add(msg);

                return Task.FromResult(WorkflowCommand<TState>.Create(
                    gotoNode: returnNode,
                    update: state
                ));
            }

            var lastMessage = state.Messages.LastOrDefault();
            // Interrupt workflow - throw special exception
            throw new WorkflowInterruptException(lastMessage?.Id ?? "", state.InterruptCaller ?? WorkflowEdges.End);
        });
    }
}
