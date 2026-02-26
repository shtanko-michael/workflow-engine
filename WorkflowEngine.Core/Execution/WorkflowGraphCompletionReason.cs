namespace WorkflowEngine.Core.Execution;

/// <summary>
/// Reason why a workflow graph execution completed.
/// </summary>
public enum WorkflowGraphCompletionReason
{
    Normal,
    Error,
    Interrupt
}
