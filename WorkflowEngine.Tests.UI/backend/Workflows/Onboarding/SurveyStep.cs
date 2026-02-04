using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.LLM;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

/// <summary>
/// Step 2: Run survey until all data (job, sphere, employees) is collected. Uses structured output; LLM composes questions.
/// </summary>
public static class SurveyStep
{
    public static async Task<WorkflowCommand<OnboardingState>> Execute(
        OnboardingState state,
        WorkflowRunnableContext context,
        Func<Exception, WorkflowCommand<OnboardingState>> errorHandler,
        WorkflowRunnableConfig config,
        IServiceScopeFactory scopeFactory)
    {
        var request = new LLMRequest
        {
            Messages = new List<LLMMessage>
            {
                new() { Role = "system", Content = OnboardingConstants.SurveySystemPrompt }
            }
        };

        foreach (var m in state.Messages)
        {
            switch (m)
            {
                case HumanMessage h:
                    request.Messages.Add(new LLMMessage { Role = "user", Content = h.Content ?? "" });
                    break;
                case AIMessage a:
                    request.Messages.Add(new LLMMessage { Role = "assistant", Content = a.Content ?? "" });
                    break;
            }
        }

        using var scope = scopeFactory.CreateScope();
        var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
        var response = await llm.ExecuteWithStructuredOutputAsync<OnboardingStructuredOutput>(
            request, model: null, CancellationToken.None).ConfigureAwait(false);

        var output = response.Output;

        if (output == null)
            throw new InvalidOperationException("No ai data");

        if (output.Completed && !string.IsNullOrWhiteSpace(output.Job) && !string.IsNullOrWhiteSpace(output.Sphere) && output.Employees.HasValue)
        {
            state.OnboardingJob = output.Job;
            state.OnboardingSphere = output.Sphere;
            state.OnboardingEmployees = output.Employees.Value;
            return WorkflowCommand<OnboardingState>.Create(
                gotoNode: "thankYou",
                update: state);
        }

        var questionToUser = output.QuestionToUser?.Trim() ?? "NO DATA";
        state.Messages.Add(new AIMessage { Content = questionToUser });
        return WorkflowCommand<OnboardingState>.Create(
            gotoNode: WorkflowEdges.AskHuman,
            update: state);
    }
}
