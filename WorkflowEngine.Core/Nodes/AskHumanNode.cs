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
            // If a human response already exists, return to the caller node
            var resumeMessage = state.Messages.LastOrDefault();
            if (resumeMessage is HumanMessage resumeHumanMessage
            //&& resumeHumanMessage.RequestId == state.InterruptRequestId
            )
            {
                var returnNode = state.InterruptCaller ?? WorkflowEdges.End;
                state.InterruptRequestId = null;
                state.InterruptCaller = null;
                state.Messages.Add(resumeHumanMessage);

                return Task.FromResult(WorkflowCommand<TState>.Create(
                    gotoNode: returnNode,
                    update: state
                ));
            }

            var lastMessage = state.Messages.LastOrDefault();
            // Interrupt workflow - throw special exception
            throw new WorkflowInterruptException(lastMessage?.Id, state.InterruptCaller ?? WorkflowEdges.End);
        });
    }
}
