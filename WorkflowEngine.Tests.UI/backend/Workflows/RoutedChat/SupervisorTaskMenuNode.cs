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
            var activeTask = TaskStackReducer.GetCurrentTask(state);
            var suspendedTasks = state.TaskStack
                .Where(x => x.Status == WorkflowEngine.Core.Supervisor.TaskStatus.Suspended)
                .Select(x => new SuspendedTaskInfo(x.TaskId, x.TaskType))
                .ToArray();

            // Only classify intent when the very last message in the list is from the user.
            // If the last message is from AI (e.g. task asked a question), we must not re-classify
            // stale history — instead ask the user for new input to avoid looping.
            var lastHuman = state.Messages.LastOrDefault() as HumanMessage;
            if (lastHuman == null || string.IsNullOrWhiteSpace(lastHuman.Content))
            {
                var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
                var menuMessage = await ms.CreateAssistantMessageAsync(config, "", CancellationToken.None);
                var menuUx = await BuildMenuMessageWithLlmAsync(scopeFactory, taskDescriptors, activeTask, suspendedTasks).ConfigureAwait(false);
                menuMessage.Content = menuUx.Message;
                menuMessage.Options = menuUx.Options;
                state.Messages.Add(menuMessage);
                await ms.NotifyStreamEndAsync(config, menuMessage.Id, menuMessage.Content, menuMessage.Options);
                state.InterruptCaller = SupervisorNodeNames.Menu;
                return WorkflowCommand<SupervisorRoutedChatState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state);
            }

            // Build conversation history: last 10 messages excluding the current lastHuman,
            // so the LLM understands the ongoing topic before deciding whether user is switching or continuing.
            var recentHistory = state.Messages
                .Where(m => !ReferenceEquals(m, lastHuman))
                .TakeLast(10)
                .Select(m => new
                {
                    role = m is HumanMessage ? "user" : "assistant",
                    content = m switch
                    {
                        HumanMessage hm => hm.Content ?? string.Empty,
                        AIMessage ai => ai.Content ?? string.Empty,
                        _ => string.Empty
                    }
                })
                .Where(m => !string.IsNullOrWhiteSpace(m.content))
                .ToArray();

            var payload = new
            {
                availableTasks = taskDescriptors
                    .Select(x => new { x.TaskType, x.Name, x.Description })
                    .OrderBy(x => x.TaskType)
                    .ToArray(),
                activeTask = activeTask == null ? null : new { activeTask.TaskId, activeTask.TaskType, activeTask.Status },
                suspendedTasks,
                conversationHistory = recentHistory
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
                        Content = $"Context: {payloadJson}\nLatest user message to classify: {lastHuman.Content}"
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

    private static async Task<(string Message, string[]? Options)> BuildMenuMessageWithLlmAsync(
        IServiceScopeFactory scopeFactory,
        IReadOnlyCollection<SupervisorTaskDescriptor> taskDescriptors,
        TaskInstance? activeTask,
        IEnumerable<SuspendedTaskInfo> suspendedTasks)
    {
        var suspendedArray = suspendedTasks.ToArray();
        var fallbackMessage = BuildMenuPrompt(taskDescriptors, activeTask, suspendedArray);
        var fallbackOptions = BuildDefaultMenuOptions(taskDescriptors, suspendedArray);

        var payload = new
        {
            availableTasks = taskDescriptors
                .Select(x => new { x.TaskType, x.Name, x.Description })
                .OrderBy(x => x.TaskType)
                .ToArray(),
            activeTask = activeTask == null ? null : new { activeTask.TaskId, activeTask.TaskType, activeTask.Status },
            suspendedTasks = suspendedArray
        };

        var request = new LLMRequest
        {
            Messages = new List<LLMMessage>
            {
                new() { Role = "system", Content = SupervisorRoutedChatConstants.MenuPresentationSystemPrompt },
                new() { Role = "user", Content = $"Context: {JsonSerializer.Serialize(payload)}" }
            }
        };

        try
        {
            using var scope = scopeFactory.CreateScope();
            var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
            var response = await llm.ExecuteWithStructuredOutputAsync<SupervisorMenuPresentationStructuredOutput>(
                request,
                model: null,
                CancellationToken.None).ConfigureAwait(false);

            var llmMessage = response.Output?.Message?.Trim();
            var llmOptions = response.Output?.Options?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(6)
                .ToArray();

            return (
                string.IsNullOrWhiteSpace(llmMessage) ? fallbackMessage : llmMessage!,
                llmOptions is { Length: > 0 } ? llmOptions : fallbackOptions);
        }
        catch
        {
            // Keep menu functional even if LLM menu rendering fails.
            return (fallbackMessage, fallbackOptions);
        }
    }

    private static string BuildMenuPrompt(
        IReadOnlyCollection<SupervisorTaskDescriptor> taskDescriptors,
        TaskInstance? activeTask,
        IEnumerable<SuspendedTaskInfo> suspendedTasks)
    {
        var suspendedList = suspendedTasks.ToArray();
        var activeLine = activeTask == null
            ? "There is no active task right now."
            : $"Active task: {activeTask.TaskType}.";

        if (taskDescriptors.Count == 0)
        {
            if (suspendedList.Length == 0)
                return $"{activeLine} No tasks are configured right now.";

            var suspendedText = string.Join(", ", suspendedList.Select(x => $"{x.TaskType} ({x.TaskId})"));
            return $"{activeLine} Suspended tasks: {suspendedText}. You can resume one of them.";
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

        if (suspendedList.Length > 0)
        {
            var suspendedText = string.Join(", ", suspendedList.Select(x => $"{x.TaskType} ({x.TaskId})"));
            return $"{activeLine}\nSuspended tasks: {suspendedText}.\nChoose one to resume, or start another task:\n{tasksList}";
        }

        return $"{activeLine}\nChoose a task:\n{tasksList}\n\nYou can also ask to switch, cancel current, or cancel all.";
    }

    private static string[]? BuildDefaultMenuOptions(
        IReadOnlyCollection<SupervisorTaskDescriptor> taskDescriptors,
        IEnumerable<SuspendedTaskInfo> suspendedTasks)
    {
        var resumeOptions = suspendedTasks
            .Select(x => $"Resume {x.TaskType} ({x.TaskId})")
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToArray();
        if (resumeOptions.Length > 0)
            return resumeOptions;

        var startOptions = taskDescriptors
            .OrderBy(x => x.TaskType)
            .Select(x => $"Start {(string.IsNullOrWhiteSpace(x.Name) ? x.TaskType : x.Name.Trim())}")
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToArray();
        return startOptions.Length > 0 ? startOptions : null;
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

    private sealed record SuspendedTaskInfo(string TaskId, string TaskType);
}
