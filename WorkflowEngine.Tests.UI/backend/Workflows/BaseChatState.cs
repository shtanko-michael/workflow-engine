using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows;

/// <summary>
/// Base state for chat-style workflows.
/// </summary>
public abstract class BaseChatState : WorkflowStateBase
{
    public string? LastUserMessage { get; set; }
}
