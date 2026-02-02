using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.LLM;

namespace WorkflowEngine.Tests.UI.Backend.Workflows;

public static class AIChatWorkflow
{
    public const string WorkflowId = "ai_chat";

    public static WorkflowDeclaration<ChatWorkflowState> Build(IServiceScopeFactory scopeFactory)
    {
        var workflow = new WorkflowGraph<ChatWorkflowState>()
            .AddNode("start", (state, ctx, errorHandler, cfg) =>
            {
                state.Messages.Add(new AIMessage
                {
                    Content = "Hello! I'm an AI assistant. Ask me anything or type \"bye\" to finish."
                });
                state.InterruptCaller = "handleInput";
                return Task.FromResult(WorkflowCommand<ChatWorkflowState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddNode("handleInput", async (state, ctx, errorHandler, cfg) =>
            {
                var lastHuman = state.Messages.LastOrDefault(message => message is HumanMessage) as HumanMessage;
                var content = lastHuman?.Content?.Trim() ?? string.Empty;
                state.LastUserMessage = content;

                if (string.Equals(content, "bye", StringComparison.OrdinalIgnoreCase))
                {
                    state.Messages.Add(new AIMessage { Content = "Goodbye!" });
                    return WorkflowCommand<ChatWorkflowState>.Create(
                        gotoNode: WorkflowEdges.End,
                        update: state);
                }

                var request = new LLMRequest
                {
                    Messages = state.Messages
                        .Select(m => m switch
                        {
                            HumanMessage h => new LLMMessage { Role = "user", Content = h.Content ?? "" },
                            AIMessage a => new LLMMessage { Role = "assistant", Content = a.Content ?? "" },
                            SystemMessage s => new LLMMessage { Role = "system", Content = s.Content ?? "" },
                            _ => null
                        })
                        .Where(x => x != null)
                        .Cast<LLMMessage>()
                        .ToList()
                };

                Func<string, Task>? streamCallback = null;
                if (cfg.Configurable.TryGetValue("stream_chunk_callback", out var callbackObj) && callbackObj is Func<string, Task> cb)
                    streamCallback = cb;

                using (var scope = scopeFactory.CreateScope())
                {
                    var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
                    var response = streamCallback != null
                        ? await llm.ExecuteStreamAsync(request, streamCallback, model: null, CancellationToken.None).ConfigureAwait(false)
                        : await llm.ExecuteAsync(request, model: null, CancellationToken.None).ConfigureAwait(false);
                    state.Messages.Add(new AIMessage { Content = response.Content ?? "" });
                }

                return WorkflowCommand<ChatWorkflowState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state);
            })
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<ChatWorkflowState>())
            .AddEdge(WorkflowEdges.Start, "start");

        return new WorkflowDeclaration<ChatWorkflowState>
        {
            Meta = new WorkflowMeta
            {
                Id = WorkflowId,
                Name = "AI Chat",
                Description = "Chat with AI (OpenAI)",
                Version = "1.0.0"
            },
            Workflow = workflow
        };
    }
}
