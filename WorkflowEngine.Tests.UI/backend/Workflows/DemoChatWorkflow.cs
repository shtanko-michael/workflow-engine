using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows;

public static class DemoChatWorkflow
{
    public const string WorkflowId = "demo_chat";

    public static WorkflowDeclaration<DemoChatState> Build()
    {
        var workflow = new WorkflowGraph<DemoChatState>()
            .AddNode("start", async (state, ctx, errorHandler, cfg) =>
            {
                var message = await ctx.Gateway.CreateAssistantMessageAsync(cfg, "", CancellationToken.None);
                message.Content = "Hello! This is a demo workflow. Ask me anything or type \"bye\" to finish.";
                state.Messages.Add(message);
                await ctx.Gateway.NotifyStreamEndAsync(cfg, message.Id, message.Content);
                state.InterruptCaller = "handleInput";
                return WorkflowCommand<DemoChatState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state);
            })
            .AddNode("handleInput", async (state, ctx, errorHandler, cfg) =>
            {
                var lastHuman = state.Messages.LastOrDefault(message => message is HumanMessage) as HumanMessage;
                var content = lastHuman?.Content?.Trim() ?? string.Empty;
                state.LastUserMessage = content;

                var message = await ctx.Gateway.CreateAssistantMessageAsync(cfg, "", CancellationToken.None);
                state.Messages.Add(message);

                if (string.Equals(content, "bye", StringComparison.OrdinalIgnoreCase))
                {
                    message.Content = "Goodbye! Workflow completed.";
                    await ctx.Gateway.NotifyStreamEndAsync(cfg, message.Id, message.Content);
                    return WorkflowCommand<DemoChatState>.Create(
                        gotoNode: WorkflowEdges.End,
                        update: state);
                }

                message.Content = $"You said: {content}";
                await ctx.Gateway.NotifyStreamEndAsync(cfg, message.Id, message.Content);
                return WorkflowCommand<DemoChatState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state);
            })
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<DemoChatState>())
            .AddEdge(WorkflowEdges.Start, "start");

        return new WorkflowDeclaration<DemoChatState>
        {
            Meta = new WorkflowMeta
            {
                Id = WorkflowId,
                Name = "Demo Chat",
                Description = "Simple echo chat for UI testing",
                Version = "1.0.0"
            },
            Workflow = workflow
        };
    }
}
