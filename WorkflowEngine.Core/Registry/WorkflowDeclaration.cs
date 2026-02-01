using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Registry;

/// <summary>
/// Declaration of a workflow
/// </summary>
public class WorkflowDeclaration<TState> where TState : WorkflowStateBase
{
    public WorkflowMeta Meta { get; set; } = null!;
    public WorkflowGraph<TState> Workflow { get; set; } = null!;
}

/// <summary>
/// Metadata about a workflow
/// </summary>
public class WorkflowMeta
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
