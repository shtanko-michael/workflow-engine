using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Contracts;
using WorkflowEngine.Tests.UI.Backend.LLM;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

/// <summary>
/// Step 3: Generate thank-you message and summary of the survey, then end the workflow.
/// </summary>
public static class ThankYouStep
{
    public static async Task<WorkflowCommand<OnboardingState>> Execute(
        OnboardingState state,
        WorkflowRunnableContext context,
        Func<Exception, WorkflowCommand<OnboardingState>> errorHandler,
        WorkflowRunnableConfig config,
        IServiceScopeFactory scopeFactory)
    {
        var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
        var message = await ms.CreateAssistantMessageAsync(config, "", CancellationToken.None);
        state.Messages.Add(message);

        var summary = $"Job: {state.OnboardingJob ?? "—"}, Industry: {state.OnboardingSphere ?? "—"}, Team size: {state.OnboardingEmployees?.ToString() ?? "—"}.";
        var prompt = $"Write a short thank-you message for completing the onboarding. Then present this summary to the user in a friendly way: {summary}";

        var request = new LLMRequest
        {
            Messages = new List<LLMMessage> { new() { Role = "user", Content = prompt } }
        };

        Func<string, Task> streamCallback = (chunk) => ms.StreamChunkAsync(config, message.Id, chunk);

        using var scope = scopeFactory.CreateScope();
        var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
        var response = await llm.ExecuteStreamAsync(request, streamCallback, model: null, CancellationToken.None).ConfigureAwait(false);

        message.Content = response.Content ?? "";
        await ms.NotifyStreamEndAsync(config, message.Id, message.Content);

        return WorkflowCommand<OnboardingState>.Create(
            gotoNode: WorkflowEdges.End,
            update: state);
    }
}
