using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Weather;

/// <summary>
/// Asks the user for a city if not yet provided; otherwise proceeds to forecast.
/// </summary>
public static class AskCityNode
{
    public static WorkflowNode<WeatherSubState> Create()
    {
        return WithContextNode.Wrap<WeatherSubState>("askCity", (state, context, errorHandler, config) =>
        {
            var lastMessage = state.Messages.LastOrDefault();
            if (lastMessage is HumanMessage humanMessage && !string.IsNullOrWhiteSpace(humanMessage.Content))
            {
                state.City = humanMessage.Content.Trim();
                return Task.FromResult(WorkflowCommand<WeatherSubState>.Create(
                    gotoNode: "forecast",
                    update: state));
            }

            state.Messages.Add(new AIMessage { Content = WeatherConstants.AskCityPrompt });
            return Task.FromResult(WorkflowCommand<WeatherSubState>.Create(
                gotoNode: WorkflowEdges.AskHuman,
                update: state));
        });
    }
}
