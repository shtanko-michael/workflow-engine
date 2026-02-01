using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
using WorkflowEngine.Examples.Onboarding;

namespace WorkflowEngine.Examples.Onboarding.Nodes;

/// <summary>
/// Complete node for onboarding workflow
/// </summary>
public static class CompleteNode
{
    public static WorkflowNode<OnboardingState> Create()
    {
        return WithContextNode.Wrap<OnboardingState>("complete", (state, ctx, errorHandler, config) =>
        {
            // Complete onboarding
            var completeMessage = new AIMessage
            {
                Content = "You're all set! Ready to build your first landing page? 🤘"
            };
            
            state.Messages.Add(completeMessage);
            state.ProgressPercent = 100;
            state.WorkflowCompleted = true;
            
            return Task.FromResult(WorkflowCommand<OnboardingState>.Create(
                gotoNode: "summary",
                update: state
            ));
        });
    }
}
