using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows;

/// <summary>
/// Shared state for chat workflows (Demo and AI).
/// </summary>
public class ChatWorkflowState : WorkflowStateBase
{
    public string? LastUserMessage { get; set; }
}
