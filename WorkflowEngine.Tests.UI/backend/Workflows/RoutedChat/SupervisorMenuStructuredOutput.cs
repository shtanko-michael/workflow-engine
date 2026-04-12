using System.Text.Json.Serialization;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// Structured output from supervisor menu decision model.
/// </summary>
public sealed class SupervisorMenuStructuredOutput
{
    [JsonPropertyName("intents")]
    public SupervisorMenuIntentOutput[]? Intents { get; set; }
}

public sealed class SupervisorMenuIntentOutput
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("taskType")]
    public string? TaskType { get; set; }

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
