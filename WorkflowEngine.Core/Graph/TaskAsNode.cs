using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Exceptions;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Extensions;
using WorkflowEngine.Core.State;
using WorkflowEngine.Core.Supervisor;

namespace WorkflowEngine.Core.Graph;

/// <summary>
/// Wraps a compiled task graph so it can be used as a node in a supervisor graph.
/// Task checkpoints are synchronized with the active task in the supervisor task stack.
/// </summary>
public static class TaskAsNode {
    public static WorkflowNode<TSupervisorState> Create<TSupervisorState>(
        CompiledWorkflowGraph<TSupervisorState> taskGraph,
        string nodeName,
        string taskType)
        where TSupervisorState : WorkflowStateBase, ISupervisorState {
        ArgumentNullException.ThrowIfNull(taskGraph);
        Validate(nodeName, taskType);

        return async (state, _, _, parentConfig) => {
            var activeTask = GetActiveTask(state, taskType);
            var childConfig = BuildChildConfig(parentConfig, nodeName, taskType, activeTask);
            var parentCommand = parentConfig.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var cmdObj)
                ? cmdObj as WorkflowCommand<TSupervisorState>
                : null;
            var childStateUpdate = string.IsNullOrEmpty(activeTask.CheckpointId) ? Activator.CreateInstance<TSupervisorState>() : null;
            if (childStateUpdate != null && state.Messages.Count > 0)
                childStateUpdate.Messages.Add(state.Messages.LastOrDefault());
            var commandToChild = WorkflowCommand<TSupervisorState>.Create(
                update: childStateUpdate,
                resume: !string.IsNullOrEmpty(activeTask.CheckpointId) ? (state.Messages.LastOrDefault() is HumanMessage ? state.Messages.LastOrDefault() : true) : null);
            parentConfig.Configurable.Remove(WorkflowConfigKeys.WorkflowCommandKey);

            await SafeNotifyTaskAsync(parentConfig, state, (i, c, s) => i.OnSubgraphStartedAsync(nodeName, c, s));
            var childState = await taskGraph.InvokeAsync(commandToChild, childConfig);
            await SafeNotifyTaskAsync(parentConfig, state, (i, c, s) => i.OnSubgraphCompletedAsync(nodeName, c, s));

            SyncTaskCheckpoint(activeTask, childConfig, childState);

            if (childState.Messages.LastOrDefault() is AIMessage aiMessage)
                state.Messages.Add(aiMessage);

            if (childState.WorkflowCompleted) {
                activeTask.Status = WorkflowEngine.Core.Supervisor.TaskStatus.Completed;
                activeTask.UpdatedAt = DateTimeOffset.UtcNow;
                return WorkflowCommand<TSupervisorState>.Create(update: childState);
            }

            if (childState.InterruptReason == WorkflowInterruptReason.AskHuman && !string.IsNullOrEmpty(childState.InterruptCaller)) {
                // Keep parent state in sync with resumed child snapshot before interrupting supervisor.
                // SyncStateSnapshot(childState, state);
                var requestId = childState.InterruptRequestId ?? childState.InterruptCaller ?? nodeName;
                // Resume supervisor from menu first so it can decide whether to continue/switch/cancel current task.
                throw new TaskWorkflowInterruptException(requestId, SupervisorNodeNames.Menu);
            }

            return SubGraphWorkflowCommand<TSupervisorState, TSupervisorState>.Create(childState, update: childState);
        };
    }

    public static WorkflowNode<TSupervisorState> CreateWithMapping<TSupervisorState, TTaskState>(
        CompiledWorkflowGraph<TTaskState> taskGraph,
        string nodeName,
        string taskType,
        Func<TSupervisorState, TaskInstance, TTaskState> initialStateMapping,
        Func<TSupervisorState, TTaskState, TaskInstance, TSupervisorState> completeStateMapping)
        where TSupervisorState : WorkflowStateBase, ISupervisorState
        where TTaskState : WorkflowStateBase {
        ArgumentNullException.ThrowIfNull(taskGraph);
        Validate(nodeName, taskType);
        ArgumentNullException.ThrowIfNull(initialStateMapping);
        ArgumentNullException.ThrowIfNull(completeStateMapping);

        return async (state, _, _, parentConfig) => {
            var activeTask = GetActiveTask(state, taskType);
            var childConfig = BuildChildConfig(parentConfig, nodeName, taskType, activeTask);
            var parentCommand = parentConfig.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var cmdObj)
                ? cmdObj as WorkflowCommand<TSupervisorState>
                : null;
            var commandToChild = WorkflowCommand<TTaskState>.Create(
                update: string.IsNullOrEmpty(activeTask.CheckpointId) ? initialStateMapping(state, activeTask) : null,
                resume: parentCommand?.Resume);
            parentConfig.Configurable.Remove(WorkflowConfigKeys.WorkflowCommandKey);

            await SafeNotifyTaskAsync(parentConfig, state, (i, c, s) => i.OnSubgraphStartedAsync(nodeName, c, s));
            var childState = await taskGraph.InvokeAsync(commandToChild, childConfig);
            await SafeNotifyTaskAsync(parentConfig, state, (i, c, s) => i.OnSubgraphCompletedAsync(nodeName, c, s));

            SyncTaskCheckpoint(activeTask, childConfig, childState);

            if (childState.WorkflowCompleted) {
                activeTask.Status = WorkflowEngine.Core.Supervisor.TaskStatus.Completed;
                activeTask.UpdatedAt = DateTimeOffset.UtcNow;
                parentConfig.SubgraphCheckpointId = null;
                parentConfig.SubgraphCheckpointNs = null;
                var merged = completeStateMapping(state, childState, activeTask);
                return SubGraphWorkflowCommand<TTaskState, TSupervisorState>.Create(childState, update: merged);
            }

            if (childState.InterruptReason == WorkflowInterruptReason.AskHuman && !string.IsNullOrEmpty(childState.InterruptCaller)) {
                var interruptUpdate = completeStateMapping(state, childState, activeTask);
                SyncStateSnapshot(interruptUpdate, state);
                parentConfig.SubgraphCheckpointId = activeTask.CheckpointId;
                parentConfig.SubgraphCheckpointNs = activeTask.CheckpointNs;
                var requestId = childState.InterruptRequestId ?? childState.InterruptCaller ?? nodeName;
                // Resume supervisor from menu first so it can decide whether to continue/switch/cancel current task.
                throw new SubgraphWorkflowInterruptException(requestId, SupervisorNodeNames.Menu);
            }

            var progressUpdate = completeStateMapping(state, childState, activeTask);
            return SubGraphWorkflowCommand<TTaskState, TSupervisorState>.Create(childState, update: progressUpdate);
        };
    }

    private static void Validate(string nodeName, string taskType) {
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required for task namespace.", nameof(nodeName));
        if (nodeName.Contains(':'))
            throw new ArgumentException("Node name cannot contain ':' (reserved for checkpoint namespace).", nameof(nodeName));
        if (string.IsNullOrWhiteSpace(taskType))
            throw new ArgumentException("Task type cannot be null or empty.", nameof(taskType));
    }

    private static TaskInstance GetActiveTask<TSupervisorState>(TSupervisorState state, string taskType)
        where TSupervisorState : ISupervisorState {
        var top = TaskStackReducer.GetCurrentTask(state);
        if (top == null)
            throw new InvalidOperationException("Active task is not available.");
        if (!string.Equals(top.TaskType, taskType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Active task type '{top.TaskType}' does not match expected '{taskType}'.");
        return top;
    }

    private static WorkflowRunnableConfig BuildChildConfig(
        WorkflowRunnableConfig parentConfig,
        string nodeName,
        string taskType,
        TaskInstance activeTask) {
        var parentNs = parentConfig.Configurable.TryGetValue(WorkflowConfigKeys.CheckpointNs, out var ns)
            ? ns?.ToString() ?? string.Empty
            : string.Empty;
        var childNs = activeTask.CheckpointNs;
        if (string.IsNullOrWhiteSpace(childNs))
            childNs = BuildTaskNamespace(parentNs, taskType, activeTask.TaskId, nodeName);

        var configurable = new Dictionary<string, object>(parentConfig.Configurable) {
            [WorkflowConfigKeys.CheckpointNs] = childNs,
            [WorkflowConfigKeys.CheckpointId] = activeTask.CheckpointId,
        };
        configurable.Remove(WorkflowConfigKeys.ParentCheckpointId);
        return new WorkflowRunnableConfig {
            Configurable = configurable,
            Context = parentConfig.Context
        };
    }

    private static string BuildTaskNamespace(string parentNs, string taskType, string taskId, string nodeName) {
        var normalizedTaskType = taskType.Replace(':', '_');
        if (string.IsNullOrEmpty(parentNs))
            return $"task:{normalizedTaskType}:{taskId}:{nodeName}";
        return $"{parentNs}:task:{normalizedTaskType}:{taskId}:{nodeName}";
    }

    private static void SyncTaskCheckpoint<TTaskState>(
        TaskInstance activeTask,
        WorkflowRunnableConfig childConfig,
        TTaskState childState) where TTaskState : WorkflowStateBase {
        activeTask.CheckpointNs = childConfig.CheckpointNs;
        activeTask.CheckpointId = childState.LastCheckpointId;
        activeTask.LastNodeId = childState.InterruptCaller;
        activeTask.UpdatedAt = DateTimeOffset.UtcNow;

        if (childState is ISupervisorState childSupervisorState) {
            var taskInChild = childSupervisorState.TaskStack.LastOrDefault(x =>
                string.Equals(x.TaskId, activeTask.TaskId, StringComparison.Ordinal));
            if (taskInChild != null) {
                taskInChild.CheckpointNs = activeTask.CheckpointNs;
                taskInChild.CheckpointId = activeTask.CheckpointId;
                taskInChild.LastNodeId = activeTask.LastNodeId;
                taskInChild.UpdatedAt = activeTask.UpdatedAt;
            }
        }
    }

    private static async Task SafeNotifyTaskAsync<TState>(
        WorkflowRunnableConfig config,
        TState state,
        Func<IWorkflowRunInterceptor, WorkflowRunnableConfig, TState, Task> invoke)
        where TState : WorkflowStateBase {
        if (config.Context?.Interceptor is not IWorkflowRunInterceptor interceptor)
            return;
        try {
            await invoke(interceptor, config, state);
        } catch {
            // Do not let interceptor break the engine.
        }
    }

    private static void SyncStateSnapshot<TState>(TState source, TState target)
        where TState : WorkflowStateBase {
        if (ReferenceEquals(source, target))
            return;

        var properties = typeof(TState).GetProperties()
            .Where(x => x.CanRead && x.CanWrite);

        foreach (var property in properties) {
            property.SetValue(target, property.GetValue(source));
        }
    }
}
