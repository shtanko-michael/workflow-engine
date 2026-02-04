using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
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
            var request = new LLMRequest
            {
                Messages = new List<LLMMessage> { new() { Role = "user", Content = RoutedChatConstants.WelcomePrompt } }
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

            return WorkflowCommand<RoutedChatState>.Create(
                gotoNode: "router",
                update: state);
        });
    }
}
