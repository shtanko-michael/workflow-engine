using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// None node: route not found; add a message and return to the router.
/// </summary>
public static class NoneNode
{
    public static WorkflowNode<RoutedChatState> Create()
    {
        return WithContextNode.Wrap<RoutedChatState>("none", (state, context, errorHandler, config) =>
        {
            state.Messages.Add(new AIMessage
            {
                Content = "I didn't get that. You can ask for a **weather forecast** (e.g. \"weather in London\") or say **onboarding** to complete the short survey."
            });
            return Task.FromResult(WorkflowCommand<RoutedChatState>.Create(
                gotoNode: "router",
                update: state));
        });
    }
}
