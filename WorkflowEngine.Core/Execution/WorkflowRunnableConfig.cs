namespace WorkflowEngine.Core.Execution;

/// <summary>
/// Configuration for workflow execution
/// </summary>
public class WorkflowRunnableConfig
{
    public Dictionary<string, object> Configurable { get; set; } = new();
    public WorkflowRunnableContext? Context { get; set; }
}
