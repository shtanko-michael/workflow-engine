using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
using WorkflowEngine.Examples.Onboarding;

namespace WorkflowEngine.Examples.Onboarding.Nodes;

/// <summary>
/// Welcome node for onboarding workflow
/// </summary>
public static class WelcomeNode
{
    public static WorkflowNode<OnboardingState> Create()
    {
        return WithContextNode.Wrap<OnboardingState>("welcome", (state, ctx, errorHandler, config) =>
        {
            // Create welcome message
            var welcomeMessage = new AIMessage
            {
                Content = "Hey! I'm Mark, your AI marketing assistant 🤘 I'll help you build a landing page and entire funnel — fast and easy."
            };
            
            state.Messages.Add(welcomeMessage);
            state.ProgressPercent = 20;
            
            return Task.FromResult(WorkflowCommand<OnboardingState>.Create(
                gotoNode: "survey",
                update: state
            ));
        });
    }
}
