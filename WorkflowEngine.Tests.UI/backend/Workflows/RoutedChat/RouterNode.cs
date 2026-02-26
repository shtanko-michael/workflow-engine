using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Contracts;
using WorkflowEngine.Tests.UI.Backend.LLM;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// Router node: if no user message yet, ask for input and go to AskHuman; otherwise classify via LLM and go to weather, onboarding, or none.
/// </summary>
public static class RouterNode
{
    public static WorkflowNode<RoutedChatState> Create(IServiceScopeFactory scopeFactory)
    {
        return WithContextNode.Wrap<RoutedChatState>("router", async (state, context, errorHandler, config) =>
        {
            var lastMessage = state.Messages.LastOrDefault();
            var hasUserInput = lastMessage is HumanMessage;

            if (!hasUserInput)
            {
                var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
                var message = await ms.CreateAssistantMessageAsync(config, "", CancellationToken.None);
                state.Messages.Add(message);

                var request = new LLMRequest
                {
                    Messages = new List<LLMMessage> { new() { Role = "user", Content = RoutedChatConstants.RouterPromptNoInput } }
                };

                Func<string, Task> streamCallback = (chunk) => ms.StreamChunkAsync(config, message.Id, chunk);

                using (var scope = scopeFactory.CreateScope())
                {
                    var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
                    var response = await llm.ExecuteStreamAsync(request, streamCallback, model: null, CancellationToken.None).ConfigureAwait(false);
                    message.Content = response.Content ?? "";
                    await ms.NotifyStreamEndAsync(config, message.Id, message.Content);
                }

                state.InterruptCaller = "router";
                return WorkflowCommand<RoutedChatState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state);
            }

            // Classify user intent
            var userContent = (lastMessage as HumanMessage)?.Content ?? "";
            var classifyRequest = new LLMRequest
            {
                Messages = new List<LLMMessage>
                {
                    new() { Role = "system", Content = RoutedChatConstants.RouterSystemPrompt },
                    new() { Role = "user", Content = userContent }
                }
            };

            using var classifyScope = scopeFactory.CreateScope();
            var classifyLlm = classifyScope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
            var classifyResponse = await classifyLlm.ExecuteWithStructuredOutputAsync<RouterStructuredOutput>(
                classifyRequest, model: null, CancellationToken.None).ConfigureAwait(false);

            var route = classifyResponse.Output?.Route?.Trim().ToLowerInvariant() ?? "none";
            if (route != "weather" && route != "onboarding" && route != "ask")
                route = "none";

            return WorkflowCommand<RoutedChatState>.Create(
                gotoNode: route,
                update: state);
        });
    }
}
