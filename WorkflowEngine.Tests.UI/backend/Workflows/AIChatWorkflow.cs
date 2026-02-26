using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Contracts;
using WorkflowEngine.Tests.UI.Backend.LLM;
using WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

namespace WorkflowEngine.Tests.UI.Backend.Workflows;

public static class AIChatWorkflow
{
    public const string WorkflowId = "ai_chat";

    public static WorkflowDeclaration<AIChatState> Build(IServiceScopeFactory scopeFactory)
    {
        var workflow = new WorkflowGraph<AIChatState>()
            .AddNode("start", async (state, ctx, errorHandler, cfg) =>
            {
                var ms = ctx.Container!.GetRequiredService<IWorkflowMessageService>();
                var message = await ms.CreateAssistantMessageAsync(cfg, "", CancellationToken.None);
                message.Content = "Hello! I'm an AI assistant. Ask me anything or type \"bye\" to finish.";
                state.Messages.Add(message);
                await ms.NotifyStreamEndAsync(cfg, message.Id, message.Content);
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

                var ms = ctx.Container!.GetRequiredService<IWorkflowMessageService>();
                var message = await ms.CreateAssistantMessageAsync(cfg, "", CancellationToken.None);
                state.Messages.Add(message);

                if (string.Equals(content, "bye", StringComparison.OrdinalIgnoreCase))
                {
                    message.Content = "Goodbye!";
                    await ms.NotifyStreamEndAsync(cfg, message.Id, message.Content);
                    return WorkflowCommand<AIChatState>.Create(
                        gotoNode: WorkflowEdges.End,
                        update: state);
                }

                var messages = new List<LLMMessage>
                {
                    new() { Role = "system", Content = AIChatConstants.ChatResponseSystemPrompt }
                };
                foreach (var m in state.Messages)
                {
                    switch (m)
                    {
                        case HumanMessage h:
                            messages.Add(new LLMMessage { Role = "user", Content = h.Content ?? "" });
                            break;
                        case AIMessage a:
                            messages.Add(new LLMMessage { Role = "assistant", Content = a.Content ?? "" });
                            break;
                        case SystemMessage s:
                            messages.Add(new LLMMessage { Role = "system", Content = s.Content ?? "" });
                            break;
                    }
                }

                var lastStreamedReplyLength = new[] { 0 };
                Func<string, Task> onAccumulatedRaw = async (accumulated) =>
                {
                    var reply = PartialJsonHelper.ExtractStringValue(accumulated, "reply");
                    if (reply.Length <= lastStreamedReplyLength[0]) return;
                    var delta = reply.Substring(lastStreamedReplyLength[0]);
                    lastStreamedReplyLength[0] = reply.Length;
                    await ms.StreamChunkAsync(cfg, message.Id, delta).ConfigureAwait(false);
                };

                using (var scope = scopeFactory.CreateScope())
                {
                    var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
                    var request = new LLMRequest { Messages = messages };
                    var response = await llm.ExecuteStreamWithStructuredOutputAsync<AIChatStructuredOutput>(
                        request, onTextChunk: null, onPartialOutput: null, onAccumulatedRaw, model: null, CancellationToken.None).ConfigureAwait(false);

                    var output = response.Output;
                    message.Content = output?.Reply?.Trim() ?? response.Content ?? "";
                    var suggestions = output?.SuggestedReplies;
                    var valid = suggestions?.Where(s => !string.IsNullOrWhiteSpace(s)).Take(6).ToArray();
                    message.Options = (valid != null && valid.Length >= 1) ? valid : null;

                    await ms.NotifyStreamEndAsync(cfg, message.Id, message.Content, message.Options);
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
