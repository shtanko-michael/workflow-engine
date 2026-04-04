using System.Text.Json;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.State;
using WorkflowEngine.Core.Supervisor;
using WorkflowEngine.Tests.UI.Backend.Contracts;
using WorkflowEngine.Tests.UI.Backend.LLM;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// Menu node that classifies user intent for task-stack actions via LLM.
/// </summary>
public static class SupervisorTaskMenuNode
{
    public static WorkflowNode<SupervisorRoutedChatState> Create(
        IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return WithContextNode.Wrap<SupervisorRoutedChatState>("supervisor_task_menu", async (state, context, _, config) =>
        {
            var taskDescriptors = GetTaskDescriptors(config);
            var allowedTaskTypes = new HashSet<string>(
                taskDescriptors.Select(x => x.TaskType),
                StringComparer.OrdinalIgnoreCase);
            // var resumeMessage = TryConsumeResumeMessage(config);
            // if (resumeMessage != null)
            // {
            //     state.Messages.Add(resumeMessage);
            // }

            var lastHuman = state.Messages.OfType<HumanMessage>().LastOrDefault();
            // Menu should classify only when there is a new incoming human message for this turn.
            // Otherwise it should ask user instead of reclassifying stale history and looping.
            if (lastHuman == null || string.IsNullOrWhiteSpace(lastHuman.Content))
            {
                var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
                var menuMessage = await ms.CreateAssistantMessageAsync(config, "", CancellationToken.None);
                menuMessage.Content = BuildMenuPrompt(taskDescriptors);
                state.Messages.Add(menuMessage);
                await ms.NotifyStreamEndAsync(config, menuMessage.Id, menuMessage.Content);
                state.InterruptCaller = SupervisorNodeNames.Menu;
                return WorkflowCommand<SupervisorRoutedChatState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state);
            }

            var activeTask = TaskStackReducer.GetCurrentTask(state);
            var suspendedTasks = state.TaskStack
                .Where(x => x.Status == WorkflowEngine.Core.Supervisor.TaskStatus.Suspended)
                .Select(x => new { x.TaskId, x.TaskType })
                .ToArray();
            var payload = new
            {
                availableTasks = taskDescriptors
                    .Select(x => new { x.TaskType, x.Name, x.Description })
                    .OrderBy(x => x.TaskType)
                    .ToArray(),
                activeTask = activeTask == null ? null : new { activeTask.TaskId, activeTask.TaskType, activeTask.Status },
                suspendedTasks
            };
            var payloadJson = JsonSerializer.Serialize(payload);
            var classifyRequest = new LLMRequest
            {
                Messages = new List<LLMMessage>
                {
                    new() { Role = "system", Content = SupervisorRoutedChatConstants.MenuSystemPrompt },
                    new()
                    {
                        Role = "user",
                        Content = $"Context: {payloadJson}\nUser message: {lastHuman.Content}"
                    }
                }
            };

            using var classifyScope = scopeFactory.CreateScope();
            var llm = classifyScope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
            var classifyResponse = await llm.ExecuteWithStructuredOutputAsync<SupervisorMenuStructuredOutput>(
                classifyRequest,
                model: null,
                CancellationToken.None).ConfigureAwait(false);

            state.MenuDecision = MapDecision(classifyResponse.Output, allowedTaskTypes, suspendedTasks.Select(x => x.TaskId).ToHashSet(StringComparer.Ordinal));
            if (!string.IsNullOrWhiteSpace(state.MenuDecision.Reason))
            {
                state.History.Add($"menu-decision:{state.MenuDecision.IntentType}:{state.MenuDecision.Reason}");
            }

            return WorkflowCommand<SupervisorRoutedChatState>.Create(
                gotoNode: SupervisorNodeNames.Intent,
                update: state,
                // use lastHuman as resume message to keep the context inside of task workflow
                resume: lastHuman);
        });
    }

    private static SupervisorTaskDescriptor[] GetTaskDescriptors(WorkflowRunnableConfig config)
    {
        if (!config.Configurable.TryGetValue(SupervisorConfigKeys.AvailableTasks, out var descriptorsObj) || descriptorsObj == null)
            return [];

        if (descriptorsObj is IReadOnlyCollection<SupervisorTaskDescriptor> typedCollection)
            return typedCollection.ToArray();

        if (descriptorsObj is IEnumerable<SupervisorTaskDescriptor> typedEnumerable)
            return typedEnumerable.ToArray();

        return [];
    }

    private static HumanMessage? TryConsumeResumeMessage(WorkflowRunnableConfig config)
    {
        if (!config.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var commandObj))
            return null;
        if (commandObj is not WorkflowCommand<SupervisorRoutedChatState> command || !command.IsResume)
            return null;
        if (command.Resume is not HumanMessage resumeMessage)
            return null;

        command.Resume = null;
        return resumeMessage;
    }

    private static string BuildMenuPrompt(IReadOnlyCollection<SupervisorTaskDescriptor> taskDescriptors)
    {
        if (taskDescriptors.Count == 0)
        {
            return "No tasks are configured right now. You can ask to continue, cancel current, cancel all, or resume a suspended task.";
        }

        var tasksList = string.Join(
            "\n",
            taskDescriptors
                .OrderBy(x => x.TaskType)
                .Select((x, index) =>
                {
                    var title = string.IsNullOrWhiteSpace(x.Name) ? x.TaskType : x.Name.Trim();
                    var description = string.IsNullOrWhiteSpace(x.Description) ? "No description." : x.Description.Trim();
                    return $"{index + 1}. {title} ({x.TaskType}) - {description}";
                }));

        return $"Choose a task:\n{tasksList}\n\nYou can also ask to switch, cancel current, cancel all, or resume a suspended task.";
    }

    private static SupervisorDecision MapDecision(
        SupervisorMenuStructuredOutput? output,
        HashSet<string> allowedTaskTypes,
        HashSet<string> suspendedTaskIds)
    {
        if (output == null || string.IsNullOrWhiteSpace(output.Action))
            return SupervisorDecision.Continue("fallback-empty");

        var action = output.Action.Trim().ToUpperInvariant();
        var taskType = output.TaskType?.Trim();
        var taskId = output.TaskId?.Trim();
        var reason = output.Reason?.Trim();

        return action switch
        {
            "CONTINUE_CURRENT" => SupervisorDecision.Continue(reason),
            "START_NEW" when !string.IsNullOrWhiteSpace(taskType) && allowedTaskTypes.Contains(taskType) =>
                SupervisorDecision.StartNew(taskType, reason),
            "SWITCH_TO" when !string.IsNullOrWhiteSpace(taskType) && allowedTaskTypes.Contains(taskType) =>
                SupervisorDecision.SwitchTo(taskType, reason),
            "CANCEL_CURRENT" => SupervisorDecision.CancelCurrent(reason),
            "CANCEL_ALL" => SupervisorDecision.CancelAll(reason),
            "RESUME_TASK" when !string.IsNullOrWhiteSpace(taskId) && suspendedTaskIds.Contains(taskId) =>
                SupervisorDecision.ResumeTask(taskId, reason),
            _ => SupervisorDecision.Continue($"fallback-invalid:{action}")
        };
    }
}
