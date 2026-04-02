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

        return decision.IntentType switch
        {
            SupervisorIntentType.ContinueCurrent => ContinueCurrent(state, options),
            SupervisorIntentType.StartNew => StartNew(state, decision.TaskType, options),
            SupervisorIntentType.SwitchTo => SwitchTo(state, decision.TaskType, options),
            SupervisorIntentType.CancelCurrent => CancelCurrent(state, options),
            SupervisorIntentType.CancelAll => CancelAll(state, options),
            SupervisorIntentType.ResumeTask => ResumeTask(state, decision.TaskId, options),
            _ => ContinueCurrent(state, options),
        };
    }

    public static TState ContinueCurrent<TState>(TState state, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        options ??= new TaskStackReducerOptions();
        EnsureNotEmpty(state, options);
        Normalize(state);
        return state;
    }

    public static TState StartNew<TState>(TState state, string? taskType, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        if (string.IsNullOrWhiteSpace(taskType))
            throw new ArgumentException("Task type cannot be null or empty.", nameof(taskType));

        options ??= new TaskStackReducerOptions();
        SuspendTop(state);
        var instance = CreateTask(taskType, options);
        instance.Status = TaskStatus.Active;
        Touch(instance);
        state.TaskStack.Add(instance);
        state.CurrentTaskId = instance.TaskId;
        Normalize(state);
        return state;
    }

    public static TState SwitchTo<TState>(TState state, string? taskType, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        if (string.IsNullOrWhiteSpace(taskType))
            throw new ArgumentException("Task type cannot be null or empty.", nameof(taskType));

        options ??= new TaskStackReducerOptions();
        var stack = state.TaskStack;
        var top = GetTop(state);
        if (top != null && string.Equals(top.TaskType, taskType, StringComparison.OrdinalIgnoreCase))
        {
            EnsureNotEmpty(state, options);
            Normalize(state);
            return state;
        }

        SuspendTop(state);

        var existingIndex = stack.FindLastIndex(x =>
            x.Status is not (TaskStatus.Cancelled or TaskStatus.Completed) &&
            string.Equals(x.TaskType, taskType, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            var existing = stack[existingIndex];
            stack.RemoveAt(existingIndex);
            existing.Status = TaskStatus.Active;
            Touch(existing);
            stack.Add(existing);
            state.CurrentTaskId = existing.TaskId;
            Normalize(state);
            return state;
        }

        return StartNew(state, taskType, options);
    }

    public static TState CancelCurrent<TState>(TState state, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        options ??= new TaskStackReducerOptions();
        var top = GetTop(state);
        if (top != null)
        {
            top.Status = TaskStatus.Cancelled;
            Touch(top);
            state.TaskStack.RemoveAt(state.TaskStack.Count - 1);
        }

        EnsureNotEmpty(state, options);
        Normalize(state);
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
        state.TaskStack.Clear();
        state.CurrentTaskId = null;
        EnsureNotEmpty(state, options);
        Normalize(state);
        return state;
    }

    public static TState ResumeTask<TState>(TState state, string? taskId, TaskStackReducerOptions? options = null)
        where TState : ISupervisorState
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task id cannot be null or empty.", nameof(taskId));

        options ??= new TaskStackReducerOptions();
        var stack = state.TaskStack;
        var existingIndex = stack.FindIndex(x =>
            string.Equals(x.TaskId, taskId, StringComparison.Ordinal) &&
            x.Status is TaskStatus.Active or TaskStatus.Suspended);
        if (existingIndex < 0)
        {
            EnsureNotEmpty(state, options);
            Normalize(state);
            return state;
        }

        SuspendTop(state);
        var existing = stack[existingIndex];
        stack.RemoveAt(existingIndex);
        existing.Status = TaskStatus.Active;
        Touch(existing);
        stack.Add(existing);
        state.CurrentTaskId = existing.TaskId;
        Normalize(state);
        return state;
    }

    public static TaskInstance? GetCurrentTask<TState>(TState state) where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
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

    private static void EnsureNotEmpty<TState>(TState state, TaskStackReducerOptions options) where TState : ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.TaskStack.Count > 0)
            return;

        var menuTask = CreateTask(options.MenuTaskType, options);
        menuTask.Status = TaskStatus.Active;
        Touch(menuTask);
        state.TaskStack.Add(menuTask);
        state.CurrentTaskId = menuTask.TaskId;
    }

    private static void SuspendTop<TState>(TState state) where TState : ISupervisorState
    {
        var top = GetTop(state);
        if (top != null && top.Status == TaskStatus.Active)
        {
            top.Status = TaskStatus.Suspended;
            Touch(top);
        }
    }

    private static void Normalize<TState>(TState state) where TState : ISupervisorState
    {
        if (state.TaskStack.Count == 0)
        {
            state.CurrentTaskId = null;
            return;
        }

        for (var i = 0; i < state.TaskStack.Count - 1; i++)
        {
            if (state.TaskStack[i].Status == TaskStatus.Active)
            {
                state.TaskStack[i].Status = TaskStatus.Suspended;
                Touch(state.TaskStack[i]);
            }
        }

        var top = state.TaskStack[^1];
        if (top.Status is not (TaskStatus.Cancelled or TaskStatus.Completed))
        {
            top.Status = TaskStatus.Active;
            Touch(top);
            state.CurrentTaskId = top.TaskId;
            return;
        }

        state.CurrentTaskId = null;
    }

    private static TaskInstance? GetTop<TState>(TState state) where TState : ISupervisorState =>
        state.TaskStack.Count == 0 ? null : state.TaskStack[^1];

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
