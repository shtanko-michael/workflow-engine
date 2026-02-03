using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Exceptions;

/// <summary>
/// Exception thrown when workflow is interrupted (e.g., for human input)
/// </summary>
public class WorkflowInterruptException : Exception
{
    public string RequestId { get; }
    public string Caller { get; }

    public WorkflowInterruptException(string requestId, string caller)
        : base("Workflow interrupted for human input")
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Caller = caller ?? throw new ArgumentNullException(nameof(caller));
    }
}

/// <summary>
/// Exception thrown when subgraph workflow is interrupted (e.g., for human input)
/// </summary>
public class SubgraphWorkflowInterruptException : Exception
{
    public string RequestId { get; }
    public string Caller { get; }

    public SubgraphWorkflowInterruptException(string requestId, string caller)
        : base("Subgraph workflow interrupted for human input")
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Caller = caller ?? throw new ArgumentNullException(nameof(caller));
    }
}