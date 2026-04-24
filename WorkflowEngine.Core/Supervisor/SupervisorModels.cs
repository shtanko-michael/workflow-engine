using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Supervisor;

public interface ISupervisorState
{
    string? CurrentTaskId { get; set; }
    List<TaskInstance> TaskStack { get; set; }
    List<TaskQueueItem> TaskQueue { get; set; }
    List<SupervisorIntentItem> PendingIntentQueue { get; set; }
    string? PendingQuestion { get; set; }
    List<string> History { get; set; }
}

public abstract class SupervisorStateBase : WorkflowStateBase, ISupervisorState
{
    public string? CurrentTaskId { get; set; }
    public List<TaskInstance> TaskStack { get; set; } = [];
    public List<TaskQueueItem> TaskQueue { get; set; } = [];
    public List<SupervisorIntentItem> PendingIntentQueue { get; set; } = [];
    public string? PendingQuestion { get; set; }
    public List<string> History { get; set; } = [];
}

public enum TaskStatus
{
    Active = 0,
    Suspended = 1,
    Cancelled = 2,
    Completed = 3,
}

public class TaskInstance
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
    public string TaskType { get; set; } = string.Empty;
    public string? SourceUserMessageId { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Active;
    public string? CheckpointNs { get; set; }
    public string? CheckpointId { get; set; }
    public string? LastNodeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TaskQueueItem
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string? SourceUserMessageId { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum SupervisorIntentType
{
    ContinueCurrent = 0,
    StartNew = 1,
    SwitchTo = 2,
    CancelCurrent = 3,
    CancelAll = 4,
    ResumeTask = 5,
}

public class SupervisorIntentItem
{
    public SupervisorIntentType IntentType { get; set; }
    public string? TaskType { get; set; }
    public string? TaskId { get; set; }
    public string? SourceUserMessageId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SupervisorDecision
{
    public SupervisorIntentType IntentType { get; set; }
    public string? TaskType { get; set; }
    public string? TaskId { get; set; }
    public string? SourceUserMessageId { get; set; }
    public string? Reason { get; set; }
    public List<SupervisorIntentItem> IntentItems { get; set; } = [];

    public static SupervisorDecision Continue(string? reason = null) =>
        new() { IntentType = SupervisorIntentType.ContinueCurrent, Reason = reason };

    public static SupervisorDecision StartNew(string taskType, string? reason = null) =>
        new() { IntentType = SupervisorIntentType.StartNew, TaskType = taskType, Reason = reason };

    public static SupervisorDecision SwitchTo(string taskType, string? reason = null) =>
        new() { IntentType = SupervisorIntentType.SwitchTo, TaskType = taskType, Reason = reason };

    public static SupervisorDecision CancelCurrent(string? reason = null) =>
        new() { IntentType = SupervisorIntentType.CancelCurrent, Reason = reason };

    public static SupervisorDecision CancelAll(string? reason = null) =>
        new() { IntentType = SupervisorIntentType.CancelAll, Reason = reason };

    public static SupervisorDecision ResumeTask(string taskId, string? reason = null) =>
        new() { IntentType = SupervisorIntentType.ResumeTask, TaskId = taskId, Reason = reason };

    public static SupervisorDecision Batch(IEnumerable<SupervisorIntentItem> items, string? reason = null) =>
        new()
        {
            IntentType = SupervisorIntentType.ContinueCurrent,
            IntentItems = items?.Where(x => x != null).ToList() ?? [],
            Reason = reason
        };
}

public static class SupervisorConfigKeys
{
    public const string SupervisorDecision = "__supervisor_decision__";
    public const string AvailableTasks = "__supervisor_available_tasks__";
}

public class SupervisorTaskDescriptor
{
    public string TaskType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool AllowMultipleInstances { get; set; } = true;
}
