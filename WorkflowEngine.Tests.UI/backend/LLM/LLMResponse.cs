namespace WorkflowEngine.Tests.UI.Backend.LLM;

/// <summary>
/// Response from LLM (plain text content).
/// </summary>
public sealed class LLMResponse
{
    public string Content { get; set; } = "";
    public string Model { get; set; } = "";
}
