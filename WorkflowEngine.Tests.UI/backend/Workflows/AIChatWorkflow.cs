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

    public static WorkflowDeclaration<AIChatState> Build(IServiceScopeFactory scopeFactory)
    {
        var workflow = new WorkflowGraph<AIChatState>()
            .AddNode("start", async (state, ctx, errorHandler, cfg) =>
            {
                var parentId = state.Messages.Count > 0 ? state.Messages[^1].Id : null;
                var message = await ctx.Gateway.CreateAssistantMessageAsync(cfg, parentId, "", CancellationToken.None);
                message.Content = "Hello! I'm an AI assistant. Ask me anything or type \"bye\" to finish.";
                state.Messages.Add(message);
                await ctx.Gateway.NotifyStreamEndAsync(cfg, message.Id, message.Content);
                state.InterruptCaller = "handleInput";
                return WorkflowCommand<AIChatState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state);
            })
            .AddNode("handleInput", async (state, ctx, errorHandler, cfg) =>
            {
                var lastHuman = state.Messages.LastOrDefault(message => message is HumanMessage) as HumanMessage;
                var content = lastHuman?.Content?.Trim() ?? string.Empty;
                state.LastUserMessage = content;

                var parentId = state.Messages.Count > 0 ? state.Messages[^1].Id : null;
                var message = await ctx.Gateway.CreateAssistantMessageAsync(cfg, parentId, "", CancellationToken.None);
                state.Messages.Add(message);

                if (string.Equals(content, "bye", StringComparison.OrdinalIgnoreCase))
                {
                    message.Content = "Goodbye!";
                    await ctx.Gateway.NotifyStreamEndAsync(cfg, message.Id, message.Content);
                    return WorkflowCommand<AIChatState>.Create(
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

                Func<string, Task> streamCallback = (chunk) => ctx.Gateway.StreamChunkAsync(cfg, message.Id, chunk);

                using (var scope = scopeFactory.CreateScope())
                {
                    var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
                    var response = await llm.ExecuteStreamAsync(request, streamCallback, model: null, CancellationToken.None).ConfigureAwait(false);
                    message.Content = response.Content ?? "";
                    await ctx.Gateway.NotifyStreamEndAsync(cfg, message.Id, message.Content);
                }

                return WorkflowCommand<AIChatState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state);
            })
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<AIChatState>())
            .AddEdge(WorkflowEdges.Start, "start");

        return new WorkflowDeclaration<AIChatState>
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
