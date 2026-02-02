namespace WorkflowEngine.Tests.UI.Backend.LLM;

/// <summary>
/// Request for LLM completion (list of messages).
/// </summary>
public sealed class LLMRequest
{
    public List<LLMMessage> Messages { get; set; } = new();
}
