using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Exceptions;

/// <summary>
/// Exception thrown when workflow is interrupted (e.g., for human input)
/// </summary>
public class WorkflowInterruptException : Exception
{
    public string RequestId { get; }
    public string ReturnToNode { get; }
    
    public WorkflowInterruptException(string requestId, string returnToNode)
        : base("Workflow interrupted for human input")
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        ReturnToNode = returnToNode ?? throw new ArgumentNullException(nameof(returnToNode));
    }
}
