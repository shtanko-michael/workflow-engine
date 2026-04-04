using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Exceptions;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Nodes;

public class AskHumanNodeOptions {
    public bool CleanResume { get; set; } = true;
}

/// <summary>
/// Node that interrupts workflow to ask for human input
/// </summary>
public static class AskHumanNode {
    /// <summary>
    /// Creates an askHuman node
    /// </summary>
    public static WorkflowNode<TState> Create<TState>(AskHumanNodeOptions options = null) where TState : WorkflowStateBase {
        return WithContextNode.Wrap<TState>(WorkflowEdges.AskHuman, (state, ctx, errorHandler, config) => {
            options ??= new AskHumanNodeOptions();
            var hasCommand = config.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var command);
            if (hasCommand
                && command is WorkflowCommand<TState> workflowCommand
                && workflowCommand.IsResume
            ) {
                var resumeMessage = workflowCommand.Resume as HumanMessage;
                // if (options.CleanResume)
                //     workflowCommand.Resume = null;
                var returnNode = state.InterruptCaller ?? WorkflowEdges.End;
                state.InterruptRequestId = null;
                state.InterruptCaller = null;
                state.InterruptReason = null;
                if (resumeMessage != null)
                    state.Messages.Add(resumeMessage);

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
