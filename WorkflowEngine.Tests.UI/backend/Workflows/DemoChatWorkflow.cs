using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows;

public static class DemoChatWorkflow
{
    public const string WorkflowId = "demo_chat";

    public static WorkflowDeclaration<ChatWorkflowState> Build()
    {
        var workflow = new WorkflowGraph<ChatWorkflowState>()
            .AddNode("start", (state, ctx, errorHandler, cfg) =>
            {
                state.Messages.Add(new AIMessage
                {
                    Content = "Hello! This is a demo workflow. Ask me anything or type \"bye\" to finish."
                });
                state.InterruptCaller = "handleInput";
                return Task.FromResult(WorkflowCommand<ChatWorkflowState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddNode("handleInput", (state, ctx, errorHandler, cfg) =>
            {
                var lastHuman = state.Messages.LastOrDefault(message => message is HumanMessage) as HumanMessage;
                var content = lastHuman?.Content?.Trim() ?? string.Empty;
                state.LastUserMessage = content;

                if (string.Equals(content, "bye", StringComparison.OrdinalIgnoreCase))
                {
                    state.Messages.Add(new AIMessage { Content = "Goodbye! Workflow completed." });
                    return Task.FromResult(WorkflowCommand<ChatWorkflowState>.Create(
                        gotoNode: WorkflowEdges.End,
                        update: state));
                }

                state.Messages.Add(new AIMessage { Content = $"You said: {content}" });
                return Task.FromResult(WorkflowCommand<ChatWorkflowState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<ChatWorkflowState>())
            .AddEdge(WorkflowEdges.Start, "start");

        return new WorkflowDeclaration<ChatWorkflowState>
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
