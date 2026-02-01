namespace WorkflowEngine.Core.Commands;

/// <summary>
/// Command for controlling workflow execution
/// </summary>
public class WorkflowCommand<TState> where TState : class
{
    /// <summary>
    /// Node name to go to
    /// </summary>
    public string? Goto { get; set; }
    
    /// <summary>
    /// State update to apply
    /// </summary>
    public TState? Update { get; set; }
    
    /// <summary>
    /// Resume data (HumanMessage or bool)
    /// </summary>
    public object? Resume { get; set; }
    
    /// <summary>
    /// Creates a new workflow command
    /// </summary>
    public static WorkflowCommand<TState> Create(
        string? gotoNode = null,
        TState? update = null,
        object? resume = null)
    {
        return new WorkflowCommand<TState>
        {
            Goto = gotoNode,
            Update = update,
            Resume = resume
        };
    }
}
