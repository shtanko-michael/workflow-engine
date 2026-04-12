namespace WorkflowEngine.Core.Supervisor;

public sealed class TaskStackReducerOptions
{
    public string MenuTaskType { get; set; } = "menu";
    public Func<string, TaskInstance>? TaskFactory { get; set; }
}

public static class TaskStackReducer
{
    public static TState Apply<TState>(TState state, SupervisorDecision decision, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(decision);
        options ??= new TaskStackReducerOptions();

        EnqueueIntents(state, ToIntentItems(decision));
        return state;
    }

    public static TState ContinueCurrent<TState>(TState state, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        options ??= new TaskStackReducerOptions();
        CleanupQueue(state);
        EnsureNotEmpty(state, options);
        NormalizeFromQueue(state, options.MenuTaskType);
        return state;
    }

    public static TState StartNew<TState>(TState state, string? taskType, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        if (string.IsNullOrWhiteSpace(taskType))
            throw new ArgumentException("Task type cannot be null or empty.", nameof(taskType));
        return StartNew(state, taskType, sourceUserMessageId: null, options);
    }

    public static TState SwitchTo<TState>(TState state, string? taskType, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        if (string.IsNullOrWhiteSpace(taskType))
            throw new ArgumentException("Task type cannot be null or empty.", nameof(taskType));

        options ??= new TaskStackReducerOptions();
        CleanupQueue(state);
        var current = GetCurrentTask(state);
        if (current != null && string.Equals(current.TaskType, taskType, StringComparison.OrdinalIgnoreCase))
        {
            EnsureNotEmpty(state, options);
            NormalizeFromQueue(state, options.MenuTaskType);
            return state;
        }

        SuspendCurrent(state);

        var existingIndex = state.TaskStack.FindLastIndex(x =>
            x.Status is not (TaskStatus.Cancelled or TaskStatus.Completed) &&
            string.Equals(x.TaskType, taskType, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            var existing = state.TaskStack[existingIndex];
            existing.Status = TaskStatus.Active;
            Touch(existing);
            MoveTaskToQueueFront(state, existing, existing.SourceUserMessageId);
            NormalizeFromQueue(state, options.MenuTaskType);
            return state;
        }

        return StartNew(state, taskType, options);
    }

    public static TState CancelCurrent<TState>(TState state, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        options ??= new TaskStackReducerOptions();
        CleanupQueue(state);
        var current = GetCurrentTask(state);
        if (current != null)
        {
            current.Status = TaskStatus.Cancelled;
            Touch(current);
            RemoveTaskFromQueue(state, current.TaskId);
        }

        EnsureNotEmpty(state, options);
        NormalizeFromQueue(state, options.MenuTaskType);
        return state;
    }

    public static TState CancelAll<TState>(TState state, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        options ??= new TaskStackReducerOptions();
        foreach (var task in state.TaskStack)
        {
            task.Status = TaskStatus.Cancelled;
            Touch(task);
        }
        state.TaskQueue.Clear();
        state.TaskStack.Clear();
        state.CurrentTaskId = null;
        EnsureNotEmpty(state, options);
        NormalizeFromQueue(state, options.MenuTaskType);
        return state;
    }

    public static TState ResumeTask<TState>(TState state, string? taskId, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task id cannot be null or empty.", nameof(taskId));

        options ??= new TaskStackReducerOptions();
        CleanupQueue(state);
        var existingIndex = state.TaskStack.FindIndex(x =>
            string.Equals(x.TaskId, taskId, StringComparison.Ordinal) &&
            x.Status is TaskStatus.Active or TaskStatus.Suspended);
        if (existingIndex < 0)
        {
            EnsureNotEmpty(state, options);
            NormalizeFromQueue(state, options.MenuTaskType);
            return state;
        }

        SuspendCurrent(state);
        var existing = state.TaskStack[existingIndex];
        existing.Status = TaskStatus.Active;
        Touch(existing);
        MoveTaskToQueueFront(state, existing, existing.SourceUserMessageId);
        NormalizeFromQueue(state, options.MenuTaskType);
        return state;
    }

    public static TaskInstance? GetCurrentTask<TState>(TState state) where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.TaskQueue.Count > 0)
        {
            var nextQueue = state.TaskQueue
                .FirstOrDefault(x => state.TaskStack.Any(t =>
                    string.Equals(t.TaskId, x.TaskId, StringComparison.Ordinal) &&
                    t.Status is TaskStatus.Active or TaskStatus.Suspended));
            if (nextQueue != null)
            {
                var fromQueue = state.TaskStack.FindLast(x =>
                    string.Equals(x.TaskId, nextQueue.TaskId, StringComparison.Ordinal) &&
                    x.Status is TaskStatus.Active or TaskStatus.Suspended);
                if (fromQueue != null)
                    return fromQueue;
            }
        }

        if (!string.IsNullOrWhiteSpace(state.CurrentTaskId))
        {
            var byCurrentId = state.TaskStack.FindLast(x =>
                string.Equals(x.TaskId, state.CurrentTaskId, StringComparison.Ordinal) &&
                x.Status == TaskStatus.Active);
            if (byCurrentId != null)
                return byCurrentId;
        }

        return state.TaskStack.FindLast(x => x.Status == TaskStatus.Active);
    }

    public static TaskQueueItem? GetCurrentQueueItem<TState>(TState state) where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
        CleanupQueue(state);
        return state.TaskQueue.FirstOrDefault();
    }

    public static TState FailCurrentAndClearQueue<TState>(TState state, string? reason = null) where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
        CleanupQueue(state);

        var now = DateTimeOffset.UtcNow;
        foreach (var queueItem in state.TaskQueue)
        {
            var task = state.TaskStack.FindLast(x => string.Equals(x.TaskId, queueItem.TaskId, StringComparison.Ordinal));
            if (task == null || task.Status is TaskStatus.Completed or TaskStatus.Cancelled)
                continue;
            task.Status = TaskStatus.Cancelled;
            task.UpdatedAt = now;
        }

        state.TaskQueue.Clear();
        state.CurrentTaskId = null;

        if (!string.IsNullOrWhiteSpace(reason))
            state.History.Add($"queue-fail-fast:{reason}");

        state.PendingIntentQueue.Clear();
        NormalizeFromQueue(state, menuTaskType: "menu");
        return state;
    }

    public static bool SuspendInterruptedAndTryMoveNext<TState>(
        TState state,
        string menuTaskType = "menu")
        where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
        CleanupQueue(state);

        var current = GetCurrentTask(state);
        if (current == null)
            return false;

        var currentQueueIndex = state.TaskQueue.FindIndex(x => string.Equals(x.TaskId, current.TaskId, StringComparison.Ordinal));
        if (currentQueueIndex < 0)
            return false;

        var hasAnotherRunnable = state.TaskQueue.Any(x => !string.Equals(x.TaskId, current.TaskId, StringComparison.Ordinal));
        if (!hasAnotherRunnable)
            return false;

        if (current.Status == TaskStatus.Active)
        {
            current.Status = TaskStatus.Suspended;
            Touch(current);
        }

        var interruptedItem = state.TaskQueue[currentQueueIndex];
        state.TaskQueue.RemoveAt(currentQueueIndex);
        interruptedItem.EnqueuedAt = DateTimeOffset.UtcNow;
        state.TaskQueue.Add(interruptedItem);

        NormalizeFromQueue(state, menuTaskType);
        var next = GetCurrentTask(state);
        return next != null && !string.Equals(next.TaskId, current.TaskId, StringComparison.Ordinal);
    }

    public static void EnqueueIntents<TState>(TState state, IEnumerable<SupervisorIntentItem> items)
        where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
        var prepared = (items ?? [])
            .Where(x => x != null)
            .Select(x =>
            {
                x.EnqueuedAt = DateTimeOffset.UtcNow;
                return x;
            })
            .ToArray();
        if (prepared.Length == 0)
            return;
        state.PendingIntentQueue.AddRange(prepared);
    }

    public static bool HasPendingIntents<TState>(TState state) where TState : ISupervisorState =>
        state.PendingIntentQueue.Count > 0;

    public static TState DrainNextIntent<TState>(TState state, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        options ??= new TaskStackReducerOptions();
        if (!TryDequeueNextIntent(state, out var nextIntent))
            return ContinueCurrent(state, options);

        return ApplySingleIntent(state, nextIntent, options);
    }

    public static bool TryDequeueNextIntent<TState>(TState state, out SupervisorIntentItem intentItem)
        where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.PendingIntentQueue.Count == 0)
        {
            intentItem = new SupervisorIntentItem { IntentType = SupervisorIntentType.ContinueCurrent };
            return false;
        }

        intentItem = state.PendingIntentQueue[0];
        state.PendingIntentQueue.RemoveAt(0);
        return true;
    }

    public static TState ApplySingleIntent<TState>(TState state, SupervisorIntentItem intentItem, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(intentItem);
        options ??= new TaskStackReducerOptions();

        return intentItem.IntentType switch
        {
            SupervisorIntentType.ContinueCurrent => ContinueCurrent(state, options),
            SupervisorIntentType.StartNew => StartNew(state, intentItem.TaskType, intentItem.SourceUserMessageId, options),
            SupervisorIntentType.SwitchTo => SwitchTo(state, intentItem.TaskType, options),
            SupervisorIntentType.CancelCurrent => CancelCurrent(state, options),
            SupervisorIntentType.CancelAll => CancelAll(state, options),
            SupervisorIntentType.ResumeTask => ResumeTask(state, intentItem.TaskId, options),
            _ => ContinueCurrent(state, options),
        };
    }

    private static void EnsureNotEmpty<TState>(TState state, TaskStackReducerOptions options) where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.TaskQueue.Count > 0)
            return;

        var menuTask = state.TaskStack.FindLast(x =>
            string.Equals(x.TaskType, options.MenuTaskType, StringComparison.OrdinalIgnoreCase) &&
            x.Status is not (TaskStatus.Cancelled or TaskStatus.Completed));
        if (menuTask == null)
        {
            menuTask = CreateTask(options.MenuTaskType, options);
            state.TaskStack.Add(menuTask);
        }

        menuTask.Status = TaskStatus.Active;
        Touch(menuTask);
        state.CurrentTaskId = menuTask.TaskId;
    }

    private static void SuspendCurrent<TState>(TState state) where TState : ISupervisorState
    {
        var current = GetCurrentTask(state);
        if (current != null && current.Status == TaskStatus.Active)
        {
            current.Status = TaskStatus.Suspended;
            Touch(current);
        }
    }

    private static void CleanupQueue<TState>(TState state) where TState : ISupervisorState
    {
        if (state.TaskQueue.Count == 0)
            return;

        var aliveIds = state.TaskStack
            .Where(x => x.Status is not (TaskStatus.Cancelled or TaskStatus.Completed))
            .Select(x => x.TaskId)
            .ToHashSet(StringComparer.Ordinal);

        state.TaskQueue = state.TaskQueue
            .Where(x => !string.IsNullOrWhiteSpace(x.TaskId) && aliveIds.Contains(x.TaskId))
            .GroupBy(x => x.TaskId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
    }

    private static void NormalizeFromQueue<TState>(TState state, string menuTaskType) where TState : ISupervisorState
    {
        CleanupQueue(state);
        if (state.TaskStack.Count == 0)
        {
            state.CurrentTaskId = null;
            return;
        }

        var currentQueue = state.TaskQueue.FirstOrDefault();
        if (currentQueue == null)
        {
            var fallbackCurrent = state.TaskStack.FindLast(x =>
                x.Status is not (TaskStatus.Cancelled or TaskStatus.Completed) &&
                string.Equals(x.TaskType, menuTaskType, StringComparison.OrdinalIgnoreCase));
            if (fallbackCurrent != null)
            {
                fallbackCurrent.Status = TaskStatus.Active;
                Touch(fallbackCurrent);
                state.CurrentTaskId = fallbackCurrent.TaskId;
            }
            else
            {
                state.CurrentTaskId = null;
            }

            foreach (var task in state.TaskStack)
            {
                if (task.Status == TaskStatus.Active && !string.Equals(task.TaskId, state.CurrentTaskId, StringComparison.Ordinal))
                {
                    task.Status = TaskStatus.Suspended;
                    Touch(task);
                }
            }
            return;
        }

        var currentTask = state.TaskStack.FindLast(x =>
            string.Equals(x.TaskId, currentQueue.TaskId, StringComparison.Ordinal) &&
            x.Status is not (TaskStatus.Cancelled or TaskStatus.Completed));
        if (currentTask == null)
        {
            state.TaskQueue.RemoveAt(0);
            NormalizeFromQueue(state, menuTaskType);
            return;
        }

        currentTask.Status = TaskStatus.Active;
        currentTask.SourceUserMessageId ??= currentQueue.SourceUserMessageId;
        Touch(currentTask);
        state.CurrentTaskId = currentTask.TaskId;

        foreach (var task in state.TaskStack)
        {
            if (string.Equals(task.TaskId, currentTask.TaskId, StringComparison.Ordinal))
                continue;
            if (task.Status is TaskStatus.Active)
            {
                task.Status = TaskStatus.Suspended;
                Touch(task);
            }
        }
    }

    private static void MoveTaskToQueueFront<TState>(TState state, TaskInstance task, string? sourceUserMessageId)
        where TState : ISupervisorState
    {
        state.TaskQueue.RemoveAll(x => string.Equals(x.TaskId, task.TaskId, StringComparison.Ordinal));
        state.TaskQueue.Insert(0, new TaskQueueItem
        {
            TaskId = task.TaskId,
            TaskType = task.TaskType,
            SourceUserMessageId = sourceUserMessageId,
            EnqueuedAt = DateTimeOffset.UtcNow,
        });
    }

    private static void RemoveTaskFromQueue<TState>(TState state, string taskId) where TState : ISupervisorState =>
        state.TaskQueue.RemoveAll(x => string.Equals(x.TaskId, taskId, StringComparison.Ordinal));

    private static IEnumerable<SupervisorIntentItem> ToIntentItems(SupervisorDecision decision)
    {
        if (decision.IntentItems.Count > 0)
            return decision.IntentItems;

        return
        [
            new SupervisorIntentItem
            {
                IntentType = decision.IntentType,
                TaskType = decision.TaskType,
                TaskId = decision.TaskId,
                SourceUserMessageId = decision.SourceUserMessageId,
                Reason = decision.Reason,
            }
        ];
    }

    private static TState StartNew<TState>(
        TState state,
        string? taskType,
        string? sourceUserMessageId,
        TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        if (string.IsNullOrWhiteSpace(taskType))
            throw new ArgumentException("Task type cannot be null or empty.", nameof(taskType));

        options ??= new TaskStackReducerOptions();
        SuspendCurrent(state);

        var instance = CreateTask(taskType, options);
        instance.SourceUserMessageId = sourceUserMessageId;
        instance.Status = TaskStatus.Suspended;
        Touch(instance);
        state.TaskStack.Add(instance);
        state.TaskQueue.Insert(0, new TaskQueueItem
        {
            TaskId = instance.TaskId,
            TaskType = instance.TaskType,
            SourceUserMessageId = sourceUserMessageId,
            EnqueuedAt = DateTimeOffset.UtcNow,
        });

        CleanupQueue(state);
        NormalizeFromQueue(state, options.MenuTaskType);
        return state;
    }

    private static TaskInstance CreateTask(string taskType, TaskStackReducerOptions options)
    {
        var instance = options.TaskFactory?.Invoke(taskType) ?? new TaskInstance { TaskType = taskType };
        if (string.IsNullOrWhiteSpace(instance.TaskId))
            instance.TaskId = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(instance.TaskType))
            instance.TaskType = taskType;
        return instance;
    }

    private static void Touch(TaskInstance instance) =>
        instance.UpdatedAt = DateTimeOffset.UtcNow;
}
