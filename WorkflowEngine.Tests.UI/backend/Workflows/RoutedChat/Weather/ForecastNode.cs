using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Contracts;
using WorkflowEngine.Tests.UI.Backend.LLM;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Weather;

/// <summary>
/// Generates a mock weather forecast for the city in state via LLM, then ends the subgraph.
/// </summary>
public static class ForecastNode
{
    public static WorkflowNode<WeatherSubState> Create(IServiceScopeFactory scopeFactory)
    {
        return WithContextNode.Wrap<WeatherSubState>("forecast", async (state, context, errorHandler, config) =>
        {
            var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
            var message = await ms.CreateAssistantMessageAsync(config, "", CancellationToken.None);
            state.Messages.Add(message);

            var city = state.City ?? "Unknown";
            var request = new LLMRequest
            {
                Messages = new List<LLMMessage>
                {
                    new() { Role = "system", Content = WeatherConstants.ForecastSystemPrompt },
                    new() { Role = "user", Content = $"City: {city}" }
                }
            };

            Func<string, Task> streamCallback = (chunk) => ms.StreamChunkAsync(config, message.Id, chunk);

            using var scope = scopeFactory.CreateScope();
            var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
            var response = await llm.ExecuteStreamAsync(request, streamCallback, model: null, CancellationToken.None).ConfigureAwait(false);

            state.Forecast = response.Content?.Trim() ?? $"No forecast for {city}.";
            message.Content = state.Forecast;
            await ms.NotifyStreamEndAsync(config, message.Id, message.Content);
            state.WorkflowCompleted = true;

            return WorkflowCommand<WeatherSubState>.Create(
                gotoNode: WorkflowEdges.End,
                update: state);
        });
    }
}
