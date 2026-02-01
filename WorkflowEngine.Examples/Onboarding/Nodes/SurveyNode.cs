using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
using WorkflowEngine.Examples.Onboarding;

namespace WorkflowEngine.Examples.Onboarding.Nodes;

/// <summary>
/// Survey node for onboarding workflow
/// </summary>
public static class SurveyNode
{
    public static WorkflowNode<OnboardingState> Create()
    {
        return WithContextNode.Wrap<OnboardingState>("survey", (state, ctx, errorHandler, config) =>
        {
            // Check if we have human input
            var lastMessage = state.Messages.LastOrDefault();
            if (lastMessage is HumanMessage humanMessage && !string.IsNullOrEmpty(humanMessage.Content))
            {
                // Process survey response
                var response = humanMessage.Content;
                
                // For MVP, just mark survey as complete
                var surveyCompleteMessage = new AIMessage
                {
                    Content = "Perfect! Give me a sec to set everything up 🤘"
                };
                
                state.Messages.Add(surveyCompleteMessage);
                state.ProgressPercent = 60;
                
                return Task.FromResult(WorkflowCommand<OnboardingState>.Create(
                    gotoNode: "complete",
                    update: state
                ));
            }
            else
            {
                // Ask survey question
                var questionMessage = new AIMessage
                {
                    Content = "What do you do? Is this your business, hobby, or a new project?"
                };
                
                state.Messages.Add(questionMessage);
                state.ProgressPercent = 40;
                
                return Task.FromResult(WorkflowCommand<OnboardingState>.Create(
                    gotoNode: "askHuman",
                    update: state
                ));
            }
        });
    }
}
