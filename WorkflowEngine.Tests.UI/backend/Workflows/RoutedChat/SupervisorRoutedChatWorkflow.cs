using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.Supervisor;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;
using WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Weather;
using WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Support;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// Supervisor workflow where menu node uses LLM to decide task-stack actions.
/// </summary>
public static class SupervisorRoutedChatWorkflow
{
    public static WorkflowDeclaration<SupervisorRoutedChatState> Build(IServiceScopeFactory scopeFactory)
    {
        static WorkflowNode<TState> InitialNode<TState>(string nextNode) where TState : WorkflowStateBase =>
            WithContextNode.Wrap<TState>("initial", (state, _, _, config) =>
            {
                if (config.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var commandObj)
                    && commandObj is WorkflowCommand<TState> command
                    && command.Resume is HumanMessage resumeHuman)
                {
                    if (state.Messages.LastOrDefault() is not HumanMessage lastHuman || lastHuman.Id != resumeHuman.Id)
                        state.Messages.Add(resumeHuman);
                    command.Resume = null;
                }

                return Task.FromResult(WorkflowCommand<TState>.Create(gotoNode: nextNode, update: state));
            });

        var weatherGraph = new WorkflowGraph<WeatherSubState>()
            .AddNode(WorkflowEdges.AskHuman, WorkflowEngine.Core.Nodes.AskHumanNode.Create<WeatherSubState>())
            .AddNode("initial", InitialNode<WeatherSubState>("askCity"))
            .AddNode("askCity", AskCityNode.Create())
            .AddNode("forecast", ForecastNode.Create(scopeFactory))
            .AddEdge(WorkflowEdges.Start, "initial");

        var scope = scopeFactory.CreateScope();
        var checkpointerFactory = scope.ServiceProvider.GetRequiredService<ICheckpointSaverFactory>();
        var compiledWeather = weatherGraph.Compile(checkpointerFactory);

        var onboardingDeclaration = OnboardingWorkflow.Build(scopeFactory);
        var compiledOnboarding = onboardingDeclaration.Workflow.Compile(checkpointerFactory);
        var taskSupportGraph = new WorkflowGraph<TaskSupportState>()
            .AddNode("initial", InitialNode<TaskSupportState>("answer"))
            .AddNode("answer", TaskSupportAnswerNode.Create(scopeFactory))
            .AddEdge(WorkflowEdges.Start, "initial");
        var compiledTaskSupport = taskSupportGraph.Compile(checkpointerFactory);

        var workflow = new SupervisorGraph<SupervisorRoutedChatState>()
            .SetMenuNode(
                SupervisorRoutedChatConstants.MenuTaskType,
                SupervisorTaskMenuNode.Create(scopeFactory))
            .SetIntentResolver((state, _, _) =>
            {
                var queuedIntents = state.MenuIntents.ToArray();
                state.MenuIntents.Clear();
                var decision = queuedIntents.Length > 0
                    ? SupervisorDecision.Batch(queuedIntents, "menu-intents")
                    : SupervisorDecision.Continue("menu-default");
                return Task.FromResult(decision);
            })
            .RegisterTask(
                SupervisorRoutedChatConstants.WeatherTaskType,
                compiledWeather,
                initialStateMapping: (parent, _) => new WeatherSubState
                {
                    // Messages = parent.Messages.Count > 0 ? [parent.Messages.Last()] : [],
                },
                completeStateMapping: (parent, sub, _) =>
                {
                    if (sub.Messages.Count > 0 && sub.Messages.Last() is { } lastMsg)
                        parent.Messages = parent.Messages.Append(lastMsg).ToList();
                    if (!string.IsNullOrEmpty(sub.City))
                        parent.RequestedForecastCities = (parent.RequestedForecastCities ?? []).Append(sub.City).ToArray();
                    return parent;
                },
                taskName: "Weather forecast",
                taskDescription: "Get weather forecast for a city")
            .RegisterTask(
                SupervisorRoutedChatConstants.OnboardingTaskType,
                compiledOnboarding,
                initialStateMapping: (parent, _) => new OnboardingState
                {
                    // Messages = parent.Messages.Count > 0 ? [parent.Messages.Last()] : [],
                },
                completeStateMapping: (parent, sub, _) =>
                {
                    if (sub.Messages.Count > 0 && sub.Messages.Last() is { } lastMsg)
                        parent.Messages = parent.Messages.Append(lastMsg).ToList();
                    var surveyEntry =
                        $"Job: {sub.OnboardingJob ?? "—"}, Industry: {sub.OnboardingSphere ?? "—"}, Team size: {sub.OnboardingEmployees?.ToString() ?? "—"}";
                    parent.SurveyResults = (parent.SurveyResults ?? []).Append(surveyEntry).ToArray();
                    return parent;
                },
                taskName: "Onboarding survey",
                taskDescription: "Run onboarding questionnaire and collect profile information")
            .RegisterTask(
                SupervisorRoutedChatConstants.TaskSupportTaskType,
                compiledTaskSupport,
                initialStateMapping: (parent, _) => new TaskSupportState
                {
                    // Messages = parent.Messages.Count > 0 ? [parent.Messages.Last()] : [],
                    TaskStackSnapshot = parent.TaskStack
                        .Select(x => new TaskSnapshotItem
                        {
                            TaskId = x.TaskId,
                            TaskType = x.TaskType,
                            Status = x.Status.ToString(),
                            UpdatedAt = x.UpdatedAt
                        })
                        .ToArray()
                },
                completeStateMapping: (parent, sub, _) =>
                {
                    if (sub.Messages.Count > 0 && sub.Messages.Last() is { } lastMsg)
                        parent.Messages = parent.Messages.Append(lastMsg).ToList();
                    return parent;
                },
                taskName: "Task support",
                taskDescription: "Answer meta questions about current tasks, statuses, and progress using internal tools.");

        return new WorkflowDeclaration<SupervisorRoutedChatState>
        {
            Meta = new WorkflowMeta
            {
                Id = SupervisorRoutedChatConstants.WorkflowId,
                Name = "Supervisor Routed Chat",
                Description = "Task-stack supervisor with LLM-driven menu decisions and intent execution node.",
                Version = "1.0.0"
            },
            Workflow = workflow.Build()
        };
    }
}
