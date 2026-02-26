using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Contracts;
using WorkflowEngine.Tests.UI.Backend.LLM;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// Welcome node: LLM generates a message listing functions and redirects to the router.
/// </summary>
public static class WelcomeNode
{
    public static WorkflowNode<RoutedChatState> Create(IServiceScopeFactory scopeFactory)
    {
        return WithContextNode.Wrap<RoutedChatState>("welcome", async (state, context, errorHandler, config) =>
        {
            var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
            var message = await ms.CreateAssistantMessageAsync(config, "", CancellationToken.None);
            state.Messages.Add(message);

            var request = new LLMRequest
            {
                Messages = new List<LLMMessage> { new() { Role = "user", Content = RoutedChatConstants.WelcomePrompt } }
            };

            Func<string, Task> streamCallback = (chunk) => ms.StreamChunkAsync(config, message.Id, chunk);

            using var scope = scopeFactory.CreateScope();
            var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
            var response = await llm.ExecuteStreamAsync(request, streamCallback, model: null, CancellationToken.None).ConfigureAwait(false);

            message.Content = response.Content ?? "";
            await ms.NotifyStreamEndAsync(config, message.Id, message.Content);

            return WorkflowCommand<RoutedChatState>.Create(
                gotoNode: "router",
                update: state);
        });
    }
}
