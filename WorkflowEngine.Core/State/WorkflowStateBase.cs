using System.Text.Json.Serialization;

namespace WorkflowEngine.Core.State;

public enum WorkflowInterruptReason
{
    AskHuman = 0,
    Error = 1,
}

/// <summary>
/// Base class for all workflow states
/// </summary>
public class WorkflowStateBase
{
    public List<WorkflowMessage> Messages { get; set; } = new();
    public string? InterruptCaller { get; set; }
    public WorkflowInterruptReason? InterruptReason { get; set; }
    public string? InterruptRequestId { get; set; }
    public string? LastCheckpointId { get; set; }
    public bool WorkflowCompleted { get; set; }
    public object? ErrorMessage { get; set; }
    public string? ErrorName { get; set; }
}

/// <summary>
/// Base class for workflow messages
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(HumanMessage), "human")]
[JsonDerivedType(typeof(AIMessage), "ai")]
[JsonDerivedType(typeof(SystemMessage), "system")]
[JsonDerivedType(typeof(RemoveMessage), "remove")]
public abstract class WorkflowMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Human message in workflow
/// </summary>
public class HumanMessage : WorkflowMessage
{
    public string? RequestId { get; set; }
    public string? Content { get; set; }
}

/// <summary>
/// AI message in workflow
/// </summary>
public class AIMessage : WorkflowMessage
{
    public string? Content { get; set; }

    /// <summary>
    /// Optional quick-reply options for the user (e.g. suggested answers to choose from).
    /// </summary>
    public string[]? Options { get; set; }
}

/// <summary>
/// System message in workflow
/// </summary>
public class SystemMessage : WorkflowMessage
{
    public string? Content { get; set; }
}

/// <summary>
/// Command to remove a message from workflow state
/// </summary>
public class RemoveMessage : WorkflowMessage
{
    public RemoveMessage(string id)
    {
        Id = id;
    }
}
