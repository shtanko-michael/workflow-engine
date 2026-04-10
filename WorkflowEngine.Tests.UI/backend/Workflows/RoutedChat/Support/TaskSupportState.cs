using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Support;

/// <summary>
/// Sub-state for generic support task that can answer meta questions about task stack.
/// </summary>
public sealed class TaskSupportState : WorkflowStateBase
{
    public IReadOnlyList<TaskSnapshotItem> TaskStackSnapshot { get; set; } = [];
}

public sealed class TaskSnapshotItem
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
