using System.Text.Json;
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
/// Ask node: simple LLM call with current state as JSON context to answer the user's question, then return to router.
/// </summary>
public static class AskNode
{
    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static WorkflowNode<RoutedChatState> Create(IServiceScopeFactory scopeFactory)
    {
        return WithContextNode.Wrap<RoutedChatState>("ask", async (state, context, errorHandler, config) =>
        {
            var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
            var lastMessage = state.Messages.LastOrDefault();
            var userContent = (lastMessage as HumanMessage)?.Content ?? "No question provided.";

            var stateJson = JsonSerializer.Serialize(state, StateJsonOptions);
            var systemContent = RoutedChatConstants.AskSystemPromptPrefix + stateJson;

            var message = await ms.CreateAssistantMessageAsync(config, "", CancellationToken.None);
            state.Messages.Add(message);

            var request = new LLMRequest
            {
                Messages = new List<LLMMessage>
                {
                    new() { Role = "system", Content = systemContent },
                    new() { Role = "user", Content = userContent }
                }
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
