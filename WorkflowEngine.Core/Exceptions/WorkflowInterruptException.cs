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
public class SubgraphWorkflowInterruptException : WorkflowInterruptException
{
	public SubgraphWorkflowInterruptException(string requestId, string caller)
	  : base(requestId, caller)
	{
	}
}

/// <summary>
/// Exception thrown when task workflow is interrupted (e.g., for human input)
/// </summary>
public class TaskWorkflowInterruptException : WorkflowInterruptException {
	public bool ContinueExecution { get; }

	public TaskWorkflowInterruptException(string requestId, string caller)
	  : this(requestId, caller, continueExecution: false) {
	}

	public TaskWorkflowInterruptException(string requestId, string caller, bool continueExecution)
	  : base(requestId, caller) {
		ContinueExecution = continueExecution;
	}
}

public class WorkflowInterruptErrorException : WorkflowInterruptException {
	public WorkflowInterruptErrorException(string requestId, string caller)
		: base(requestId, caller) {
	}
}