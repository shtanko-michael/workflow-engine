using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Contracts;
using WorkflowEngine.Tests.UI.Backend.LLM;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Support;

/// <summary>
/// One-shot support task: can use internal tools and answers then completes.
/// </summary>
public static class TaskSupportAnswerNode
{
    public static WorkflowNode<TaskSupportState> Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return WithContextNode.Wrap<TaskSupportState>("task_support_answer", async (state, context, _, config) =>
        {
            var lastHuman = state.Messages.LastOrDefault() as HumanMessage;
            var question = lastHuman?.Content?.Trim() ?? string.Empty;

            var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
            var message = await ms.CreateAssistantMessageAsync(config, "", CancellationToken.None);
            state.Messages.Add(message);

            var request = new LLMRequest
            {
                Messages = new List<LLMMessage>
                {
                    new() { Role = "system", Content = TaskSupportConstants.ToolCallingSystemPrompt },
                    new()
                    {
                        Role = "user",
                        Content = question
                    }
                },
                ToolChoice = "auto",
                MaxToolIterations = 4,
                Tools = new List<LLMToolDefinition>
                {
                    new()
                    {
                        Name = TaskSupportConstants.GetTaskStackStateToolName,
                        Description = "Get all current tasks and statuses with aggregate counters.",
                        ParametersJsonSchema = JsonSerializer.Serialize(new
                        {
                            type = "object",
                            properties = new { },
                            additionalProperties = false
                        }),
                        ExecuteAsync = _ => Task.FromResult(GetTaskStackStateTool.Execute(state))
                    }
                }
            };

            using var scope = scopeFactory.CreateScope();
            var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
            var finalResponse = await llm.ExecuteWithToolsAsync(request, model: null, CancellationToken.None)
                .ConfigureAwait(false);

            message.Content = finalResponse.Content?.Trim();
            if (string.IsNullOrWhiteSpace(message.Content))
            {
                message.Content = "I could not generate a full answer right now.";
            }

            await ms.NotifyStreamEndAsync(config, message.Id, message.Content);
            state.WorkflowCompleted = true;
            return WorkflowCommand<TaskSupportState>.Create(gotoNode: WorkflowEdges.End, update: state);
        });
    }
}
