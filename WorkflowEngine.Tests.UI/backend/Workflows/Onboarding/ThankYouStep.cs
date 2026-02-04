using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.State;
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
        var summary = $"Job: {state.OnboardingJob ?? "—"}, Industry: {state.OnboardingSphere ?? "—"}, Team size: {state.OnboardingEmployees?.ToString() ?? "—"}.";
        var prompt = $"Write a short thank-you message for completing the onboarding. Then present this summary to the user in a friendly way: {summary}";

        var request = new LLMRequest
        {
            Messages = new List<LLMMessage> { new() { Role = "user", Content = prompt } }
        };

        Func<string, Task>? streamCallback = null;
        if (config.Configurable.TryGetValue("stream_chunk_callback", out var callbackObj) && callbackObj is Func<string, Task> cb)
            streamCallback = cb;

        using var scope = scopeFactory.CreateScope();
        var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
        var response = streamCallback != null
            ? await llm.ExecuteStreamAsync(request, streamCallback, model: null, CancellationToken.None).ConfigureAwait(false)
            : await llm.ExecuteAsync(request, model: null, CancellationToken.None).ConfigureAwait(false);

        state.Messages.Add(new AIMessage { Content = response.Content ?? "" });

        return WorkflowCommand<OnboardingState>.Create(
            gotoNode: WorkflowEdges.End,
            update: state);
    }
}
