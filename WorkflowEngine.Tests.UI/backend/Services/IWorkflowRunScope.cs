namespace WorkflowEngine.Tests.UI.Backend.Services;

/// <summary>
/// Scoped context for the current workflow run. Set by ChatWorkflowServiceNew before execution.
/// </summary>
public interface IWorkflowRunScope
{
	string? ConversationId { get; set; }
}
