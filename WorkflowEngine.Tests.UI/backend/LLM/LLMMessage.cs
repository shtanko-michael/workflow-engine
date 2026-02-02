namespace WorkflowEngine.Tests.UI.Backend.LLM;

/// <summary>
/// Single message for LLM request (role + content).
/// </summary>
public sealed class LLMMessage
{
    public string Role { get; set; } = "user"; // system, user, assistant
    public string Content { get; set; } = "";
}
