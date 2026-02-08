using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Commands;

/// <summary>
/// Command for controlling workflow execution
/// </summary>
public class WorkflowCommand<TState> where TState : WorkflowStateBase
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

    public bool IsResume => Resume != null;

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

public class SubGraphWorkflowCommand<TChildState, TParentState> : WorkflowCommand<TParentState>
    where TParentState : WorkflowStateBase
    where TChildState : WorkflowStateBase
{
    TChildState ChildState { get; set; }

    /// <summary>
    /// Creates a new workflow command
    /// </summary>
    public static SubGraphWorkflowCommand<TChildState, TParentState> Create(
        TChildState state,
        string? gotoNode = null,
        TParentState? update = null,
        object? resume = null)
    {
        return new SubGraphWorkflowCommand<TChildState, TParentState>
        {
            ChildState = state,
            Goto = gotoNode,
            Update = update,
            Resume = resume
        };
    }
}
