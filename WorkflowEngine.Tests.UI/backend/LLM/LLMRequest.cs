namespace WorkflowEngine.Tests.UI.Backend.LLM;

/// <summary>
/// Request for LLM completion (list of messages).
/// </summary>
public sealed class LLMRequest
{
    public List<LLMMessage> Messages { get; set; } = new();

    /// <summary>
    /// Optional callable tools for the model. If empty, request is plain chat completion.
    /// </summary>
    public List<LLMToolDefinition> Tools { get; set; } = new();

    /// <summary>
    /// Tool choice policy: "auto" (default), "required", or "none".
    /// </summary>
    public string ToolChoice { get; set; } = "auto";

    /// <summary>
    /// Safety cap for iterative tool calls.
    /// </summary>
    public int MaxToolIterations { get; set; } = 4;
}

/// <summary>
/// Tool descriptor + executor used by tool-calling flows.
/// </summary>
public sealed class LLMToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// JSON schema (object) for tool arguments.
    /// </summary>
    public string ParametersJsonSchema { get; set; } = """{"type":"object","properties":{},"additionalProperties":false}""";

    /// <summary>
    /// Runtime executor. Input is JSON arguments string, output is JSON/plain text result.
    /// </summary>
    public Func<string, Task<string>>? ExecuteAsync { get; set; }
}
