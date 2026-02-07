using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.LLM;
using WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;
using WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Weather;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// Routed chat workflow: welcome -> router (LLM) -> askHuman | weather subgraph | onboarding subgraph | none.
/// Closed loop: after weather/onboarding/none we return to the router.
/// </summary>
public static class RoutedChatWorkflow
{
    /// <summary>
    /// Builds the workflow declaration. Requires checkpointer to compile subgraphs (weather, onboarding).
    /// </summary>
    public static WorkflowDeclaration<RoutedChatState> Build(
        IServiceScopeFactory scopeFactory)
    {
        // Weather subgraph: ask city -> AskHuman or forecast (LLM) -> End
        var weatherGraph = new WorkflowGraph<WeatherSubState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<WeatherSubState>())
            .AddNode("askCity", AskCityNode.Create())
            .AddNode("forecast", ForecastNode.Create(scopeFactory))
            .AddEdge(WorkflowEdges.Start, "askCity");

        var scope = scopeFactory.CreateScope();
        var checkpointerFactory = scope.ServiceProvider.GetRequiredService<ICheckpointSaverFactory>();
        var compiledWeather = weatherGraph.Compile(checkpointerFactory);

        // Onboarding subgraph: dedicated onboarding state
        var onboardingDeclaration = OnboardingWorkflow.Build(scopeFactory);
        var compiledOnboarding = onboardingDeclaration.Workflow.Compile(checkpointerFactory);

        var mainGraph = new WorkflowGraph<RoutedChatState>()
            .AddNode("welcome", WelcomeNode.Create(scopeFactory))
            .AddNode("router", RouterNode.Create(scopeFactory))
            .AddNode(WorkflowEdges.ErrorHandler, ErrorHandlerNode.Create<RoutedChatState>())
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<RoutedChatState>())
            .AddNode("ask", AskNode.Create(scopeFactory))
            .AddNode("none", NoneNode.Create())
            .AddNode(
                "weather",
                compiledWeather,
                initialStateMapping: p => new WeatherSubState
                {
                    Messages = p.Messages.Count > 0 ? [p.Messages.Last()] : [],
                },
                completeStateMapping: (p, s) =>
                {
                    if (s.Messages.Count > 0 && s.Messages.Last() is { } lastMsg)
                        p.Messages = p.Messages.Append(lastMsg).ToList();
                    if (!string.IsNullOrEmpty(s.City))
                        p.RequestedForecastCities = (p.RequestedForecastCities ?? []).Append(s.City).ToArray();
                    return p;
                })
            .AddNode(
                "onboarding",
                compiledOnboarding,
                initialStateMapping: p => new OnboardingState
                {
                    Messages = p.Messages.Count > 0 ? [p.Messages.Last()] : [],
                },
                completeStateMapping: (p, s) =>
                {
                    if (s.Messages.Count > 0 && s.Messages.Last() is { } lastMsg)
                        p.Messages = p.Messages.Append(lastMsg).ToList();
                    var surveyEntry = $"Job: {s.OnboardingJob ?? "—"}, Industry: {s.OnboardingSphere ?? "—"}, Team size: {s.OnboardingEmployees?.ToString() ?? "—"}";
                    p.SurveyResults = (p.SurveyResults ?? []).Append(surveyEntry).ToArray();
                    return p;
                })
            .AddEdge(WorkflowEdges.Start, "welcome")
            .AddEdge("welcome", "router")
            .AddEdge("router", WorkflowEdges.AskHuman)
            .AddEdge("router", "ask")
            .AddEdge("router", "weather")
            .AddEdge("router", "onboarding")
            .AddEdge("router", "none")
            .AddEdge(WorkflowEdges.AskHuman, "router")
            .AddEdge("ask", "router")
            .AddEdge("none", "router")
            .AddEdge("weather", "router")
            .AddEdge("onboarding", "router");

        return new WorkflowDeclaration<RoutedChatState>
        {
            Meta = new WorkflowMeta
            {
                Id = RoutedChatConstants.WorkflowId,
                Name = "Routed Chat",
                Description = "Router with weather forecast and onboarding subgraphs; LLM routes to weather, onboarding, or fallback.",
                Version = "1.0.0"
            },
            Workflow = mainGraph
        };
    }
}
