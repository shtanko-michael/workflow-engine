using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

/// <summary>
/// Delegate for onboarding step methods (state, context, errorHandler, config, scopeFactory) -> command.
/// </summary>
internal delegate Task<WorkflowCommand<OnboardingState>> StepRunner(
    OnboardingState state,
    WorkflowRunnableContext context,
    Func<Exception, WorkflowCommand<OnboardingState>> errorHandler,
    WorkflowRunnableConfig config,
    IServiceScopeFactory scopeFactory);

/// <summary>
/// Onboarding workflow: welcome -> survey (loop until complete) -> thank you -> end.
/// </summary>
public static class OnboardingWorkflow
{
    public static WorkflowDeclaration<OnboardingState> Build(IServiceScopeFactory scopeFactory)
    {
        WorkflowNode<OnboardingState> Step(string name, StepRunner run) =>
            WithContextNode.Wrap<OnboardingState>(name, (s, c, e, cfg) => run(s, c, e, cfg, scopeFactory));

        var workflow = new WorkflowGraph<OnboardingState>()
            .AddNode("initial", WithContextNode.Wrap<OnboardingState>("initial", (state, _, _, config) =>
            {
                if (config.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var commandObj)
                    && commandObj is WorkflowCommand<OnboardingState> command
                    && command.Resume is HumanMessage resumeHuman)
                {
                    if (state.Messages.LastOrDefault() is not HumanMessage lastHuman || lastHuman.Id != resumeHuman.Id)
                        state.Messages.Add(resumeHuman);
                    command.Resume = null;
                }

                return Task.FromResult(WorkflowCommand<OnboardingState>.Create(gotoNode: "welcome", update: state));
            }))
            .AddNode("welcome", Step("welcome", WelcomeStep.Execute))
            .AddNode("survey", Step("survey", SurveyStep.Execute))
            .AddNode("thankYou", Step("thankYou", ThankYouStep.Execute))
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<OnboardingState>())
            .AddEdge(WorkflowEdges.Start, "initial");

        return new WorkflowDeclaration<OnboardingState>
        {
            Meta = new WorkflowMeta
            {
                Id = OnboardingConstants.WorkflowId,
                Name = "Onboarding",
                Description = "Short onboarding survey to tailor the system to the user",
                Version = "1.0.0"
            },
            Workflow = workflow
        };
    }
}
