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
    public static async Task<WorkflowCommand<OnboardingState>> Execute(
        OnboardingState state,
        WorkflowRunnableContext context,
        Func<Exception, WorkflowCommand<OnboardingState>> errorHandler,
        WorkflowRunnableConfig config,
        IServiceScopeFactory scopeFactory)
    {
        var parentId = state.Messages.Count > 0 ? state.Messages[^1].Id : null;
        var message = await context.Gateway.CreateAssistantMessageAsync(config, parentId, "", CancellationToken.None);
        state.Messages.Add(message);

        var request = new LLMRequest
        {
            Messages = new List<LLMMessage> { new() { Role = "user", Content = OnboardingConstants.WelcomePrompt } }
        };

        Func<string, Task> streamCallback = (chunk) => context.Gateway.StreamChunkAsync(config, message.Id, chunk);

        using var scope = scopeFactory.CreateScope();
        var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
        var response = await llm.ExecuteStreamAsync(request, streamCallback, model: null, CancellationToken.None).ConfigureAwait(false);

        message.Content = response.Content ?? "";
        await context.Gateway.NotifyStreamEndAsync(config, message.Id, message.Content);

        return WorkflowCommand<OnboardingState>.Create(
            gotoNode: "survey",
            update: state);
    }
}
