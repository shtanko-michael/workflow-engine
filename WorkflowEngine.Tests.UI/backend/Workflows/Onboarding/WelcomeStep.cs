using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.LLM;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

/// <summary>
/// Step 1: Generate welcome message inviting the user to complete onboarding (no wait for user; transition to survey).
/// </summary>
public static class WelcomeStep
{
    public static async Task<WorkflowCommand<ChatWorkflowState>> Execute(
        ChatWorkflowState state,
        WorkflowRunnableContext context,
        Func<Exception, WorkflowCommand<ChatWorkflowState>> errorHandler,
        WorkflowRunnableConfig config,
        IServiceScopeFactory scopeFactory)
    {
        var request = new LLMRequest
        {
            Messages = new List<LLMMessage> { new() { Role = "user", Content = OnboardingConstants.WelcomePrompt } }
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

        return WorkflowCommand<ChatWorkflowState>.Create(
            gotoNode: "survey",
            update: state);
    }
}
