using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
using WorkflowEngine.Examples.Onboarding;

namespace WorkflowEngine.Examples.Onboarding.Nodes;

/// <summary>
/// Summary node for onboarding workflow
/// </summary>
public static class SummaryNode
{
    public static WorkflowNode<OnboardingState> Create()
    {
        return WithContextNode.Wrap<OnboardingState>("summary", (state, ctx, errorHandler, config) =>
        {
            // Create summary message
            var summaryMessage = new AIMessage
            {
                Content = "Onboarding complete! Your workspace is ready."
            };
            
            state.Messages.Add(summaryMessage);

            return Task.FromResult(WorkflowCommand<OnboardingState>.Create(
                gotoNode: WorkflowEdges.End,
                update: state
            ));
        });
    }
}
